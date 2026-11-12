//! # aid-identity
//!
//! AlliedID 의 보안 모델: **비밀번호가 없다. 기기가 열쇠다.**
//!
//! * 기기 최초 부팅 시 Ed25519 키쌍을 만든다 (`DeviceIdentity::init`).
//! * 키 파일에는 하드웨어 지문(Pi 시리얼 / machine-id)이 함께 봉인된다.
//!   키 파일을 다른 기기로 복사하면 지문이 맞지 않아 열리지 않는다.
//! * 공개키에서 국가번호 없는 12자리 **AlliedID 번호**가 파생된다. 이 번호가
//!   전화번호이자 계정이다 (`AidNumber`).
//! * 권한은 기기 키로 서명된 `Grant` 토큰이다. "문은 돌릴 수 있지만,
//!   권한이 없으면 돌아가지 않는다" → `DeviceIdentity::check`.

mod grant;
mod number;

pub use grant::{Capability, Grant, GrantStore, STANDARD_CAPABILITIES};
pub use number::AidNumber;

use base64::{engine::general_purpose::STANDARD as B64, Engine};
use ed25519_dalek::{Signature, Signer, SigningKey, Verifier, VerifyingKey};
use rand_core::OsRng;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fs;
use std::path::{Path, PathBuf};

pub const KEY_FILE: &str = "device.key";
/// 누구나 읽을 수 있는 공개 정보 (번호, 공개키). 권한 확인은 이것만으로 가능하다.
pub const PUBLIC_FILE: &str = "public.json";
pub const GRANTS_DIR: &str = "grants";

#[derive(Debug, thiserror::Error)]
pub enum IdentityError {
    #[error("I/O error at {path}: {source}")]
    Io { path: PathBuf, source: std::io::Error },
    #[error("device identity not initialised (run `aid identity init`)")]
    NotInitialised,
    #[error("device identity already exists at {0} (use --force to replace)")]
    AlreadyExists(PathBuf),
    #[error("key file is bound to another device (hardware fingerprint mismatch)")]
    WrongDevice,
    #[error("malformed key file: {0}")]
    Malformed(String),
    #[error("invalid AlliedID number: {0}")]
    InvalidNumber(String),
    #[error("signature verification failed")]
    BadSignature,
}

pub type Result<T> = std::result::Result<T, IdentityError>;

fn io_err(path: &Path) -> impl FnOnce(std::io::Error) -> IdentityError + '_ {
    move |source| IdentityError::Io { path: path.to_path_buf(), source }
}

/// 키 파일 디스크 포맷.
#[derive(Serialize, Deserialize)]
struct KeyFile {
    version: u32,
    /// Ed25519 seed (32 bytes, base64)
    seed: String,
    /// SHA-256(hardware id), hex
    hw_fingerprint: String,
}

/// 이 기기의 하드웨어 식별자.
///
/// 우선순위: `AID_HW_ID` 환경변수(테스트/VM용) → Raspberry Pi SoC 시리얼
/// → `/etc/machine-id`.
pub fn hardware_id() -> String {
    if let Ok(v) = std::env::var("AID_HW_ID") {
        if !v.is_empty() {
            return v;
        }
    }
    for p in ["/proc/device-tree/serial-number", "/sys/firmware/devicetree/base/serial-number"] {
        if let Ok(s) = fs::read_to_string(p) {
            let s = s.trim_matches(|c: char| c == '\0' || c.is_whitespace());
            if !s.is_empty() {
                return format!("rpi:{s}");
            }
        }
    }
    match fs::read_to_string("/etc/machine-id") {
        Ok(s) if !s.trim().is_empty() => format!("machine-id:{}", s.trim()),
        _ => "unknown".to_string(),
    }
}

pub fn fingerprint(hw_id: &str) -> String {
    hex(&Sha256::digest(format!("alliedid/hw/v1/{hw_id}").as_bytes()))
}

pub(crate) fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|b| format!("{b:02x}")).collect()
}

/// 상태 디렉터리 결정: `AID_STATE_DIR` → `/var/lib/alliedid`(존재 시) → `~/.local/share/alliedid`.
pub fn default_state_dir() -> PathBuf {
    if let Ok(d) = std::env::var("AID_STATE_DIR") {
        if !d.is_empty() {
            return PathBuf::from(d);
        }
    }
    let system = PathBuf::from("/var/lib/alliedid");
    if system.is_dir() {
        return system;
    }
    let base = std::env::var("XDG_DATA_HOME")
        .map(PathBuf::from)
        .unwrap_or_else(|_| PathBuf::from(std::env::var("HOME").unwrap_or_else(|_| ".".into())).join(".local/share"));
    base.join("alliedid")
}

/// 기기 신원. 이것이 계정이자 번호이자 열쇠.
pub struct DeviceIdentity {
    key: SigningKey,
    state_dir: PathBuf,
}

impl DeviceIdentity {
    /// 새 기기 키를 생성하고 현재 하드웨어에 봉인한다.
    pub fn init(state_dir: &Path, force: bool) -> Result<Self> {
        let path = state_dir.join(KEY_FILE);
        if path.exists() && !force {
            return Err(IdentityError::AlreadyExists(path));
        }
        fs::create_dir_all(state_dir).map_err(io_err(state_dir))?;
        let key = SigningKey::generate(&mut OsRng);
        let kf = KeyFile {
            version: 1,
            seed: B64.encode(key.to_bytes()),
            hw_fingerprint: fingerprint(&hardware_id()),
        };
        write_private(&path, serde_json::to_string_pretty(&kf).unwrap().as_bytes())?;
        let id = Self { key, state_dir: state_dir.to_path_buf() };
        id.write_public(serde_json::Value::Null)?;
        Ok(id)
    }

    /// 공개 정보 파일(0644) 기록. `extra` 에 파생 공개값(예: WireGuard 공개키)을 담는다.
    pub fn write_public(&self, extra: serde_json::Value) -> Result<()> {
        let p = PublicFile {
            version: 1,
            public_key: self.public_key_b64(),
            number: self.number().digits(),
            extra,
        };
        let path = self.state_dir.join(PUBLIC_FILE);
        fs::write(&path, serde_json::to_string_pretty(&p).unwrap()).map_err(io_err(&path))
    }

    /// 비밀키 없이 쓰는 공개 신원.
    pub fn public(&self) -> PublicIdentity {
        PublicIdentity { key: self.verifying_key(), state_dir: self.state_dir.clone(), extra: serde_json::Value::Null }
    }

    /// 기존 키를 연다. 다른 기기에서 복사된 키라면 `WrongDevice`.
    pub fn load(state_dir: &Path) -> Result<Self> {
        let path = state_dir.join(KEY_FILE);
        if !path.exists() {
            return Err(IdentityError::NotInitialised);
        }
        let raw = fs::read_to_string(&path).map_err(io_err(&path))?;
        let kf: KeyFile = serde_json::from_str(&raw).map_err(|e| IdentityError::Malformed(e.to_string()))?;
        if kf.hw_fingerprint != fingerprint(&hardware_id()) {
            return Err(IdentityError::WrongDevice);
        }
        let seed = B64.decode(kf.seed.trim()).map_err(|e| IdentityError::Malformed(e.to_string()))?;
        let seed: [u8; 32] = seed.try_into().map_err(|_| IdentityError::Malformed("seed must be 32 bytes".into()))?;
        Ok(Self { key: SigningKey::from_bytes(&seed), state_dir: state_dir.to_path_buf() })
    }

    pub fn state_dir(&self) -> &Path {
        &self.state_dir
    }

    pub fn verifying_key(&self) -> VerifyingKey {
        self.key.verifying_key()
    }

    pub fn public_key_b64(&self) -> String {
        B64.encode(self.verifying_key().to_bytes())
    }

    /// 국가번호 없는 AlliedID 번호.
    pub fn number(&self) -> AidNumber {
        AidNumber::from_public_key(&self.verifying_key())
    }

    /// 다른 키(예: WireGuard)를 결정적으로 파생할 때 쓰는 32바이트 비밀.
    /// 같은 기기 → 같은 파생 키. 도메인 문자열로 용도를 분리한다.
    pub fn derive_secret(&self, domain: &str) -> [u8; 32] {
        let mut h = Sha256::new();
        h.update(b"alliedid/derive/v1/");
        h.update(domain.as_bytes());
        h.update([0u8]);
        h.update(self.key.to_bytes());
        h.finalize().into()
    }

    pub fn sign(&self, msg: &[u8]) -> Signature {
        self.key.sign(msg)
    }

    /// 권한 부여. `subject_key` 가 None 이면 이 기기 자신에게.
    pub fn grant(&self, cap: Capability, subject_key: Option<VerifyingKey>, ttl_secs: Option<u64>) -> Grant {
        let subject = subject_key.unwrap_or_else(|| self.verifying_key());
        Grant::issue(self, cap, &subject, ttl_secs)
    }

    pub fn grants(&self) -> GrantStore {
        GrantStore::new(self.state_dir.join(GRANTS_DIR))
    }

    /// 문 손잡이를 돌려본다: 이 기기가 `cap` 권한을 가졌는가?
    pub fn check(&self, cap: &Capability) -> bool {
        self.public().check(cap)
    }
}

#[derive(Serialize, Deserialize)]
struct PublicFile {
    version: u32,
    public_key: String,
    number: String,
    #[serde(default, skip_serializing_if = "serde_json::Value::is_null")]
    extra: serde_json::Value,
}

/// 공개 신원: 번호와 공개키만 안다. 권한을 **확인**할 수는 있지만 **부여**할 수는 없다.
///
/// OS 에서는 `device.key` 가 root 전용(0600)이고 `public.json`·`grants/` 는 누구나 읽을 수
/// 있지만 root 만 쓸 수 있다. 그래서 일반 앱은 문을 돌려볼 수만 있고 열쇠를 복제할 수 없다.
pub struct PublicIdentity {
    key: VerifyingKey,
    state_dir: PathBuf,
    /// `write_public` 의 extra (예: `wg_public_key`)
    pub extra: serde_json::Value,
}

impl PublicIdentity {
    pub fn load(state_dir: &Path) -> Result<Self> {
        let path = state_dir.join(PUBLIC_FILE);
        if !path.exists() {
            return Err(IdentityError::NotInitialised);
        }
        let raw = fs::read_to_string(&path).map_err(io_err(&path))?;
        let p: PublicFile = serde_json::from_str(&raw).map_err(|e| IdentityError::Malformed(e.to_string()))?;
        let key = parse_public_key(&p.public_key)?;
        if AidNumber::from_public_key(&key).digits() != p.number {
            return Err(IdentityError::Malformed("public.json number does not match key".into()));
        }
        Ok(Self { key, state_dir: state_dir.to_path_buf(), extra: p.extra })
    }

    pub fn verifying_key(&self) -> VerifyingKey {
        self.key
    }

    pub fn public_key_b64(&self) -> String {
        B64.encode(self.key.to_bytes())
    }

    pub fn number(&self) -> AidNumber {
        AidNumber::from_public_key(&self.key)
    }

    pub fn grants(&self) -> GrantStore {
        GrantStore::new(self.state_dir.join(GRANTS_DIR))
    }

    /// 이 기기가 스스로 발급하고(issuer) 스스로 받은(subject), 유효한 권한만 문을 연다.
    pub fn check(&self, cap: &Capability) -> bool {
        let me = self.key;
        let now = grant::now();
        self.grants().list().unwrap_or_default().iter().any(|g| {
            g.issuer_key().ok() == Some(me) && g.subject_key().ok() == Some(me) && g.verify_at(now).is_ok() && g.capability.covers(cap)
        })
    }

    /// 유효한 권한 이름 목록
    pub fn capabilities(&self) -> Vec<Capability> {
        let me = self.key;
        let now = grant::now();
        self.grants()
            .list()
            .unwrap_or_default()
            .into_iter()
            .filter(|g| g.issuer_key().ok() == Some(me) && g.subject_key().ok() == Some(me) && g.verify_at(now).is_ok())
            .map(|g| g.capability)
            .collect()
    }
}

pub fn parse_public_key(b64: &str) -> Result<VerifyingKey> {
    let bytes = B64.decode(b64.trim()).map_err(|e| IdentityError::Malformed(e.to_string()))?;
    let arr: [u8; 32] = bytes.try_into().map_err(|_| IdentityError::Malformed("public key must be 32 bytes".into()))?;
    VerifyingKey::from_bytes(&arr).map_err(|e| IdentityError::Malformed(e.to_string()))
}

pub fn verify(key: &VerifyingKey, msg: &[u8], sig: &Signature) -> Result<()> {
    key.verify(msg, sig).map_err(|_| IdentityError::BadSignature)
}

fn write_private(path: &Path, data: &[u8]) -> Result<()> {
    use std::io::Write;
    let tmp = path.with_extension("tmp");
    {
        let mut opts = fs::OpenOptions::new();
        opts.write(true).create(true).truncate(true);
        #[cfg(unix)]
        {
            use std::os::unix::fs::OpenOptionsExt;
            opts.mode(0o600);
        }
        let mut f = opts.open(&tmp).map_err(io_err(&tmp))?;
        f.write_all(data).map_err(io_err(&tmp))?;
        f.sync_all().map_err(io_err(&tmp))?;
    }
    fs::rename(&tmp, path).map_err(io_err(path))
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::Mutex;

    // 테스트들이 AID_HW_ID 환경변수를 공유하므로 직렬화한다.
    pub(crate) static ENV_LOCK: Mutex<()> = Mutex::new(());

    #[test]
    fn init_load_roundtrip_keeps_number() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let dir = tempfile::tempdir().unwrap();
        let a = DeviceIdentity::init(dir.path(), false).unwrap();
        let b = DeviceIdentity::load(dir.path()).unwrap();
        assert_eq!(a.number(), b.number());
        assert!(matches!(DeviceIdentity::init(dir.path(), false), Err(IdentityError::AlreadyExists(_))));
    }

    #[test]
    fn copied_key_does_not_open_on_other_device() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let dir = tempfile::tempdir().unwrap();
        DeviceIdentity::init(dir.path(), false).unwrap();
        std::env::set_var("AID_HW_ID", "test-device-B");
        assert!(matches!(DeviceIdentity::load(dir.path()), Err(IdentityError::WrongDevice)));
    }

    #[test]
    fn door_turns_only_with_permission() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let dir = tempfile::tempdir().unwrap();
        let id = DeviceIdentity::init(dir.path(), false).unwrap();
        let call: Capability = "call".parse().unwrap();
        assert!(!id.check(&call), "no grant yet → door does not turn");
        let g = id.grant("net.*".parse().unwrap(), None, None);
        id.grants().save(&g).unwrap();
        assert!(id.check(&"net.vpn".parse().unwrap()));
        assert!(!id.check(&call));
        id.grants().save(&id.grant(call.clone(), None, None)).unwrap();
        assert!(id.check(&call));
    }

    #[test]
    fn expired_or_forged_grants_are_rejected() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let dir = tempfile::tempdir().unwrap();
        let id = DeviceIdentity::init(dir.path(), false).unwrap();
        let mut g = id.grant("call".parse().unwrap(), None, Some(10));
        assert!(g.verify_at(g.issued_at + 5).is_ok());
        assert!(g.verify_at(g.issued_at + 11).is_err());
        g.capability = "system.admin".parse().unwrap(); // 위조
        assert!(g.verify_at(g.issued_at).is_err());
    }

    #[test]
    fn public_identity_checks_without_secret() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let dir = tempfile::tempdir().unwrap();
        let id = DeviceIdentity::init(dir.path(), false).unwrap();
        id.grants().save(&id.grant("call".parse().unwrap(), None, None)).unwrap();
        std::fs::remove_file(dir.path().join(KEY_FILE)).unwrap(); // 비밀키가 없어도
        let p = PublicIdentity::load(dir.path()).unwrap();
        assert_eq!(p.number(), id.number());
        assert!(p.check(&"call".parse().unwrap()));
        assert!(!p.check(&"system.admin".parse().unwrap()));
        assert_eq!(p.capabilities().len(), 1);
    }

    #[test]
    fn grant_from_foreign_issuer_does_not_open_door() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let a = tempfile::tempdir().unwrap();
        let b = tempfile::tempdir().unwrap();
        let me = DeviceIdentity::init(a.path(), false).unwrap();
        let stranger = DeviceIdentity::init(b.path(), false).unwrap();
        // 남의 기기가 나에게 system.admin 을 "발급"해서 내 grants 폴더에 넣어도
        let forged = stranger.grant("system.admin".parse().unwrap(), Some(me.verifying_key()), None);
        me.grants().save(&forged).unwrap();
        assert!(!me.check(&"system.admin".parse().unwrap()));
    }

    #[test]
    fn derived_secrets_are_domain_separated() {
        let _g = ENV_LOCK.lock().unwrap();
        std::env::set_var("AID_HW_ID", "test-device-A");
        let dir = tempfile::tempdir().unwrap();
        let id = DeviceIdentity::init(dir.path(), false).unwrap();
        assert_ne!(id.derive_secret("wireguard"), id.derive_secret("sip"));
        assert_eq!(id.derive_secret("wireguard"), id.derive_secret("wireguard"));
    }
}
