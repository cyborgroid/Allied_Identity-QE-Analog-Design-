//! `aid` — AlliedID 26.11.1 LTS 시스템 명령.
//!
//! 셸(C#)과 블록 편집기는 이 명령의 `--json` 출력을 통해 시스템과 대화한다.

use aid_identity::{default_state_dir, parse_public_key, AidNumber, Capability, DeviceIdentity, IdentityError, PublicIdentity};
use aid_mode::{detect, write_state, Mode, Paths};
use aid_net::location::{self, ApDatabase};
use aid_net::vpn::{self, Peer, PeerBook};
use aid_net::wifi;
use clap::{Parser, Subcommand};
use serde_json::json;
use std::path::PathBuf;
use std::process::ExitCode;
use std::time::Duration;

const VERSION: &str = "26.11.1 LTS";

#[derive(Parser)]
#[command(name = "aid", version = VERSION, about = "AlliedID Desktop Phone OS — 기기가 열쇠다")]
struct Cli {
    /// 상태 디렉터리 (기본: $AID_STATE_DIR → /var/lib/alliedid → ~/.local/share/alliedid)
    #[arg(long, global = true)]
    state_dir: Option<PathBuf>,
    /// JSON 으로 출력
    #[arg(long, global = true)]
    json: bool,
    #[command(subcommand)]
    cmd: Cmd,
}

#[derive(Subcommand)]
enum Cmd {
    /// 기기 신원 (= 계정 = 전화번호)
    Identity {
        #[command(subcommand)]
        cmd: IdentityCmd,
    },
    /// 이 기기에 권한 부여 (`--to` 로 다른 기기에 부여)
    Grant {
        capability: String,
        /// 받는 기기의 Ed25519 공개키(base64)
        #[arg(long)]
        to: Option<String>,
        /// 유효 기간(일)
        #[arg(long)]
        days: Option<u64>,
    },
    /// 권한 회수
    Revoke { capability: String },
    /// 문 손잡이 돌려보기: 권한이 있으면 exit 0, 없으면 exit 1
    Check { capability: String },
    /// 부여된 권한 목록
    Grants,
    /// 표준 권한 설명
    Capabilities,
    /// Wi-Fi
    Wifi {
        #[command(subcommand)]
        cmd: WifiCmd,
    },
    /// Wi-Fi + GPS 위치 추정
    Locate {
        /// GPS 고정이 있으면 주변 AP 위치를 학습
        #[arg(long)]
        learn: bool,
        #[arg(long, default_value = "127.0.0.1:2947")]
        gpsd: String,
    },
    /// 국경 없는 오버레이 VPN
    Vpn {
        #[command(subcommand)]
        cmd: VpnCmd,
    },
    /// AlliedID 번호로 전화 걸기 (SIP over overlay)
    Call { number: String },
    /// 번호 유효성 확인 및 오버레이 주소 조회
    Lookup { number: String },
    /// Phone / Desktop 모드
    Mode {
        #[command(subcommand)]
        cmd: Option<ModeCmd>,
    },
    /// 셸용 종합 상태
    Status,
}

#[derive(Subcommand)]
enum IdentityCmd {
    /// 최초 부팅 시 기기 키 생성 + 기본 권한 부여
    Init {
        #[arg(long)]
        force: bool,
        /// 기본 권한을 부여하지 않음
        #[arg(long)]
        bare: bool,
    },
    Show,
}

#[derive(Subcommand)]
enum WifiCmd {
    Scan {
        #[arg(long)]
        rescan: bool,
    },
}

#[derive(Subcommand)]
enum VpnCmd {
    /// 이 기기의 WireGuard 공개키와 오버레이 주소
    Info,
    /// wg-quick 설정 출력 (`> /etc/wireguard/aid0.conf`)
    Config {
        #[arg(long, default_value_t = vpn::WG_PORT)]
        port: u16,
    },
    /// 피어 추가
    AddPeer {
        number: String,
        wg_public_key: String,
        #[arg(long)]
        endpoint: Option<String>,
        #[arg(long)]
        relay: bool,
    },
}

#[derive(Subcommand)]
enum ModeCmd {
    /// 모드 감시 데몬: 변화가 있을 때마다 상태 파일 갱신
    Watch {
        #[arg(long, default_value = "/run/alliedid/mode.json")]
        out: PathBuf,
        #[arg(long, default_value_t = 1000)]
        interval_ms: u64,
    },
}

/// 첫 부팅 시 기본으로 열리는 문들. system.admin 은 의도적으로 제외.
const DEFAULT_GRANTS: &[&str] = &["call", "net.*", "apps.install", "desktop.edit"];

type R<T> = Result<T, Box<dyn std::error::Error>>;

fn main() -> ExitCode {
    let cli = Cli::parse();
    match run(cli) {
        Ok(code) => code,
        Err(e) => {
            eprintln!("aid: {e}");
            ExitCode::from(2)
        }
    }
}

fn print(json_mode: bool, v: serde_json::Value, human: impl FnOnce() -> String) {
    if json_mode {
        println!("{}", serde_json::to_string_pretty(&v).unwrap());
    } else {
        println!("{}", human());
    }
}

fn run(cli: Cli) -> R<ExitCode> {
    let state = cli.state_dir.clone().unwrap_or_else(default_state_dir);
    let j = cli.json;
    // 비밀키가 필요한 작업(권한 부여, VPN 설정)은 root 만, 확인은 누구나.
    let load = || DeviceIdentity::load(&state);
    let public = || PublicIdentity::load(&state);

    match cli.cmd {
        Cmd::Identity { cmd: IdentityCmd::Init { force, bare } } => {
            let id = DeviceIdentity::init(&state, force)?;
            id.write_public(json!({"wg_public_key": vpn::wg_keys(&id).public_b64}))?;
            if !bare {
                for c in DEFAULT_GRANTS {
                    id.grants().save(&id.grant(c.parse()?, None, None))?;
                }
            }
            let n = id.number();
            print(j, json!({"number": n.digits(), "display": n.display(), "state_dir": state}), || {
                format!("새 기기 신원이 생성되었습니다.\n  번호: {n}\n  위치: {}", state.display())
            });
        }
        Cmd::Identity { cmd: IdentityCmd::Show } => {
            let id = public()?;
            let n = id.number();
            // 비밀키를 열 수 있으면(=root, 올바른 기기) 하드웨어 봉인도 확인
            let sealed = match load() {
                Ok(_) => json!(true),
                Err(IdentityError::WrongDevice) => json!(false),
                Err(_) => json!(null),
            };
            print(
                j,
                json!({"number": n.digits(), "display": n.display(), "public_key": id.public_key_b64(), "sealed_to_this_device": sealed}),
                || format!("{n}\n공개키: {}\n기기 봉인: {}", id.public_key_b64(), match sealed.as_bool() { Some(true) => "✓", Some(false) => "✗ 다른 기기의 키", None => "(root 로 확인)" }),
            );
        }
        Cmd::Grant { capability, to, days } => {
            let id = load()?;
            let subject = to.as_deref().map(parse_public_key).transpose()?;
            let g = id.grant(capability.parse()?, subject, days.map(|d| d * 86_400));
            if subject.is_none() {
                let p = id.grants().save(&g)?;
                print(j, json!({"granted": capability, "file": p}), || format!("권한 부여: {capability}"));
            } else {
                // 다른 기기용 토큰은 저장하지 않고 출력 → 상대 기기에 전달
                println!("{}", serde_json::to_string_pretty(&g)?);
            }
        }
        Cmd::Revoke { capability } => {
            let n = load()?.grants().revoke(&capability.parse()?)?;
            print(j, json!({"revoked": n}), || format!("{n}개 회수됨"));
        }
        Cmd::Check { capability } => {
            let cap: Capability = capability.parse()?;
            let ok = match public() {
                Ok(id) => id.check(&cap),
                Err(IdentityError::NotInitialised) => false,
                Err(e) => return Err(e.into()),
            };
            print(j, json!({"capability": cap, "allowed": ok}), || {
                if ok { format!("🔓 {cap}: 문이 돌아갑니다") } else { format!("🔒 {cap}: 권한이 없어 돌아가지 않습니다") }
            });
            return Ok(if ok { ExitCode::SUCCESS } else { ExitCode::from(1) });
        }
        Cmd::Grants => {
            let id = public()?;
            let me = id.verifying_key();
            let gs: Vec<_> = id
                .grants()
                .list()?
                .into_iter()
                .map(|g| {
                    let valid = g.verify_at(now()).is_ok() && g.issuer_key().ok() == Some(me);
                    json!({"capability": g.capability, "expires_at": g.expires_at, "valid": valid})
                })
                .collect();
            print(j, json!(gs), || {
                gs.iter()
                    .map(|g| format!("{} {}", if g["valid"] == true { "✓" } else { "✗" }, g["capability"].as_str().unwrap_or("")))
                    .collect::<Vec<_>>()
                    .join("\n")
            });
        }
        Cmd::Capabilities => {
            let caps = aid_identity::STANDARD_CAPABILITIES;
            print(j, json!(caps.iter().map(|(c, d)| json!({"capability": c, "description": d})).collect::<Vec<_>>()), || {
                caps.iter().map(|(c, d)| format!("{c:<14} {d}")).collect::<Vec<_>>().join("\n")
            });
        }
        Cmd::Wifi { cmd: WifiCmd::Scan { rescan } } => {
            require(&state, "net.wifi")?;
            let aps = wifi::scan(rescan)?;
            print(j, json!(aps), || {
                aps.iter()
                    .map(|a| format!("{}{:>3}%  {:<32} {}  {} MHz", if a.in_use { "*" } else { " " }, a.signal, a.ssid, a.bssid, a.freq_mhz))
                    .collect::<Vec<_>>()
                    .join("\n")
            });
        }
        Cmd::Locate { learn, gpsd } => {
            require(&state, "net.location")?;
            // AP 위치 DB 는 사용자 데이터 (시스템 상태 폴더는 root 전용 쓰기)
            let db_path = user_data_dir().join("ap-db.json");
            let mut db = ApDatabase::load(&db_path)?;
            let scan = wifi::scan(false).unwrap_or_default();
            let gps = location::read_gpsd(&gpsd, Duration::from_secs(3))?;
            let mut learned = 0;
            if learn {
                if let Some(g) = &gps {
                    learned = db.learn(g, &scan);
                    db.save(&db_path)?;
                }
            }
            let fix = location::fuse(location::estimate_wifi(&scan, &db), gps);
            print(j, json!({"fix": fix, "aps_seen": scan.len(), "aps_known": db.aps.len(), "learned": learned}), || match &fix {
                Some(f) => format!("{:.6}, {:.6}  ±{:.0}m  ({:?})", f.lat, f.lon, f.accuracy_m, f.source),
                None => format!("위치를 알 수 없음 (AP {}개 보임, DB {}개)", scan.len(), db.aps.len()),
            });
        }
        Cmd::Vpn { cmd } => {
            let book_path = state.join("peers.json");
            let mut book: PeerBook = std::fs::read_to_string(&book_path).ok().and_then(|s| serde_json::from_str(&s).ok()).unwrap_or_default();
            match cmd {
                VpnCmd::Info => {
                    let p = public()?;
                    let n = p.number();
                    let wg = match load() {
                        Ok(id) => vpn::wg_keys(&id).public_b64,
                        Err(_) => p.extra["wg_public_key"].as_str().unwrap_or("-").to_string(),
                    };
                    print(j, json!({"number": n.digits(), "wg_public_key": wg, "ipv6": vpn::overlay_v6(&n), "ipv4": vpn::overlay_v4(&n), "interface": vpn::INTERFACE, "peers": book.peers.len()}), || {
                        format!("{n}\nWireGuard: {wg}\nIPv6: {}\nIPv4: {}\n피어: {}개", vpn::overlay_v6(&n), vpn::overlay_v4(&n), book.peers.len())
                    });
                }
                VpnCmd::Config { port } => {
                    require(&state, "net.vpn")?;
                    let id = load()?;
                    print!("{}", vpn::wg_quick_config(&id, &book, port));
                }
                VpnCmd::AddPeer { number, wg_public_key, endpoint, relay } => {
                    require(&state, "net.vpn")?;
                    let number: AidNumber = number.parse()?;
                    book.peers.retain(|p| p.number != number);
                    book.peers.push(Peer { number, wg_public_key, endpoint, relay });
                    std::fs::write(&book_path, serde_json::to_string_pretty(&book)?)?;
                    print(j, json!({"peers": book.peers.len()}), || format!("피어 추가: {number} (총 {}개)", book.peers.len()));
                }
            }
        }
        Cmd::Lookup { number } => {
            let n: AidNumber = number.parse()?;
            print(j, json!({"number": n.digits(), "display": n.display(), "ipv6": vpn::overlay_v6(&n), "ipv4": vpn::overlay_v4(&n), "sip": sip_uri(&n)}), || {
                format!("{n}\n  overlay: {} / {}\n  sip: {}", vpn::overlay_v6(&n), vpn::overlay_v4(&n), sip_uri(&n))
            });
        }
        Cmd::Call { number } => {
            require(&state, "call")?;
            let n: AidNumber = number.parse()?;
            let uri = sip_uri(&n);
            if which("baresip") {
                let st = std::process::Command::new("baresip").args(["-e", &format!("/dial {uri}")]).status()?;
                return Ok(if st.success() { ExitCode::SUCCESS } else { ExitCode::from(1) });
            }
            print(j, json!({"dial": uri, "dialer": null}), || format!("{uri}\n(baresip 가 설치되어 있지 않아 URI 만 출력합니다)"));
        }
        Cmd::Mode { cmd: None } => {
            let s = detect(&Paths::default());
            print(j, json!(s), || format!("{}{}", s.mode, if s.forced { " (forced)" } else { "" }));
        }
        Cmd::Mode { cmd: Some(ModeCmd::Watch { out, interval_ms }) } => {
            let paths = Paths::default();
            let mut last: Option<Mode> = None;
            loop {
                let s = detect(&paths);
                if last != Some(s.mode) {
                    write_state(&out, &s)?;
                    eprintln!("aid-moded: mode → {}", s.mode);
                    last = Some(s.mode);
                }
                std::thread::sleep(Duration::from_millis(interval_ms));
            }
        }
        Cmd::Status => {
            let mode = detect(&Paths::default());
            let v = match public() {
                Ok(id) => {
                    let n = id.number();
                    let caps: Vec<String> = id.capabilities().into_iter().map(|c| c.to_string()).collect();
                    json!({"version": VERSION, "initialised": true, "number": n.digits(), "display": n.display(),
                           "ipv6": vpn::overlay_v6(&n), "mode": mode.mode, "capabilities": caps})
                }
                Err(e) => json!({"version": VERSION, "initialised": false, "error": e.to_string(), "mode": mode.mode}),
            };
            print(j, v.clone(), || serde_json::to_string_pretty(&v).unwrap());
        }
    }
    Ok(ExitCode::SUCCESS)
}

fn require(state: &std::path::Path, cap: &str) -> R<()> {
    let id = PublicIdentity::load(state)?;
    if id.check(&cap.parse()?) {
        Ok(())
    } else {
        Err(format!("🔒 '{cap}' 권한이 없습니다 (aid grant {cap})").into())
    }
}

fn user_data_dir() -> PathBuf {
    std::env::var_os("XDG_DATA_HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from(std::env::var_os("HOME").unwrap_or_else(|| ".".into())).join(".local/share"))
        .join("alliedid")
}

fn sip_uri(n: &AidNumber) -> String {
    format!("sip:{}@[{}]", n.digits(), vpn::overlay_v6(n))
}

fn which(bin: &str) -> bool {
    std::env::var_os("PATH").map(|p| std::env::split_paths(&p).any(|d| d.join(bin).is_file())).unwrap_or(false)
}

fn now() -> u64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0)
}
