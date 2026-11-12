//! # aid-mode — Phone ↔ Desktop 전환
//!
//! "Phone 으로 대할 때는 Phone 이다. 거기에 모니터 연결하면 Desktop 이 된다."
//!
//! `/sys/class/drm/card*-<CONNECTOR>/status` 를 읽어서
//! * 외부 디스플레이(HDMI/DP/VGA/DVI/Virtual)가 연결돼 있으면 → `Desktop`
//! * 내장 패널(DSI/DPI/eDP/LVDS)만 있거나 아무 것도 없으면 → `Phone`
//!
//! 강제 설정 우선순위: 커널 인자 `alliedid.mode=phone|desktop` → `/etc/alliedid/mode` 파일.

use serde::{Deserialize, Serialize};
use std::fs;
use std::path::{Path, PathBuf};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Mode {
    Phone,
    Desktop,
}

impl std::fmt::Display for Mode {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(match self {
            Mode::Phone => "phone",
            Mode::Desktop => "desktop",
        })
    }
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum ConnectorKind {
    /// 케이스에 붙은 내장 터치스크린
    Internal,
    /// 모니터
    External,
    Unknown,
}

#[derive(Clone, Debug, PartialEq, Eq, Serialize, Deserialize)]
pub struct Connector {
    pub name: String,
    pub kind: ConnectorKind,
    pub connected: bool,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct ModeState {
    pub mode: Mode,
    pub forced: bool,
    pub connectors: Vec<Connector>,
}

pub fn classify(name: &str) -> ConnectorKind {
    let n = name.to_ascii_uppercase();
    if ["DSI", "DPI", "EDP", "LVDS", "SPI"].iter().any(|p| n.starts_with(p)) {
        ConnectorKind::Internal
    } else if ["HDMI", "DP", "VGA", "DVI", "VIRTUAL", "COMPOSITE", "USB"].iter().any(|p| n.starts_with(p)) {
        ConnectorKind::External
    } else {
        ConnectorKind::Unknown
    }
}

/// `/sys/class/drm` (또는 테스트용 루트)에서 커넥터 목록을 읽는다.
pub fn read_connectors(drm_root: &Path) -> Vec<Connector> {
    let mut v: Vec<Connector> = fs::read_dir(drm_root)
        .into_iter()
        .flatten()
        .flatten()
        .filter_map(|e| {
            let file = e.file_name().to_string_lossy().to_string();
            // card1-HDMI-A-1 → HDMI-A-1
            let (card, conn) = file.split_once('-')?;
            if !card.starts_with("card") {
                return None;
            }
            let status = fs::read_to_string(e.path().join("status")).ok()?;
            Some(Connector { name: conn.to_string(), kind: classify(conn), connected: status.trim() == "connected" })
        })
        .collect();
    v.sort_by(|a, b| a.name.cmp(&b.name));
    v
}

fn parse_mode(s: &str) -> Option<Mode> {
    match s.trim() {
        "phone" => Some(Mode::Phone),
        "desktop" => Some(Mode::Desktop),
        _ => None,
    }
}

/// 강제 설정 파일 읽기. `auto`/없음 → None.
pub fn read_override(path: &Path) -> Option<Mode> {
    parse_mode(&fs::read_to_string(path).ok()?)
}

/// 커널 명령줄의 `alliedid.mode=` (GRUB 메뉴 "Phone 모드 강제").
pub fn read_cmdline(path: &Path) -> Option<Mode> {
    fs::read_to_string(path).ok()?.split_whitespace().find_map(|a| a.strip_prefix("alliedid.mode=").and_then(parse_mode))
}

pub fn decide(connectors: &[Connector], forced: Option<Mode>) -> ModeState {
    let mode = forced.unwrap_or_else(|| {
        if connectors.iter().any(|c| c.connected && c.kind == ConnectorKind::External) {
            Mode::Desktop
        } else {
            Mode::Phone
        }
    });
    ModeState { mode, forced: forced.is_some(), connectors: connectors.to_vec() }
}

pub struct Paths {
    pub drm_root: PathBuf,
    pub override_file: PathBuf,
    pub cmdline: PathBuf,
}

impl Default for Paths {
    fn default() -> Self {
        Self { drm_root: "/sys/class/drm".into(), override_file: "/etc/alliedid/mode".into(), cmdline: "/proc/cmdline".into() }
    }
}

pub fn detect(paths: &Paths) -> ModeState {
    let forced = read_cmdline(&paths.cmdline).or_else(|| read_override(&paths.override_file));
    decide(&read_connectors(&paths.drm_root), forced)
}

/// 상태 파일을 원자적으로 기록 (셸이 inotify/폴링으로 읽는다).
pub fn write_state(path: &Path, state: &ModeState) -> std::io::Result<()> {
    if let Some(p) = path.parent() {
        fs::create_dir_all(p)?;
    }
    let tmp = path.with_extension("tmp");
    fs::write(&tmp, serde_json::to_string_pretty(state).unwrap())?;
    fs::rename(tmp, path)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn fake_drm(entries: &[(&str, &str)]) -> tempfile::TempDir {
        let d = tempfile::tempdir().unwrap();
        for (name, status) in entries {
            let p = d.path().join(name);
            fs::create_dir_all(&p).unwrap();
            fs::write(p.join("status"), format!("{status}\n")).unwrap();
        }
        fs::create_dir_all(d.path().join("card1")).unwrap(); // 커넥터 아닌 항목
        d
    }

    #[test]
    fn phone_on_internal_screen_only() {
        let d = fake_drm(&[("card1-DSI-1", "connected"), ("card1-HDMI-A-1", "disconnected")]);
        assert_eq!(decide(&read_connectors(d.path()), None).mode, Mode::Phone);
    }

    #[test]
    fn desktop_when_monitor_plugged() {
        let d = fake_drm(&[("card1-DSI-1", "connected"), ("card1-HDMI-A-1", "connected")]);
        assert_eq!(decide(&read_connectors(d.path()), None).mode, Mode::Desktop);
    }

    #[test]
    fn vmware_virtual_display_is_desktop() {
        let d = fake_drm(&[("card0-Virtual-1", "connected")]);
        assert_eq!(decide(&read_connectors(d.path()), None).mode, Mode::Desktop);
    }

    #[test]
    fn kernel_cmdline_forces_mode() {
        let d = fake_drm(&[("card0-Virtual-1", "connected")]);
        let cmd = d.path().join("cmdline");
        fs::write(&cmd, "BOOT_IMAGE=/casper/vmlinuz boot=casper alliedid.mode=phone ---\n").unwrap();
        let s = detect(&Paths { drm_root: d.path().into(), override_file: d.path().join("none"), cmdline: cmd });
        assert_eq!(s.mode, Mode::Phone);
        assert!(s.forced);
    }

    #[test]
    fn override_wins() {
        let d = fake_drm(&[("card0-Virtual-1", "connected")]);
        let s = decide(&read_connectors(d.path()), Some(Mode::Phone));
        assert_eq!(s.mode, Mode::Phone);
        assert!(s.forced);
    }
}
