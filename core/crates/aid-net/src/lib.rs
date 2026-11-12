//! # aid-net
//!
//! * [`wifi`]     — 주변 Wi-Fi 탐색 (`nmcli` 결과 파싱). Wi-Fi 는 "기기 탐색용".
//! * [`location`] — Wi-Fi 신호 + GPS(gpsd) 융합 위치 추정.
//! * [`vpn`]      — AlliedID 번호 → 오버레이 주소, WireGuard 설정 생성.
//!   국가 구분 없는 하나의 사설망 = "국경 없는 VPN".

pub mod location;
pub mod vpn;
pub mod wifi;

#[derive(Debug, thiserror::Error)]
pub enum NetError {
    #[error("command `{cmd}` failed: {msg}")]
    Command { cmd: String, msg: String },
    #[error("I/O: {0}")]
    Io(#[from] std::io::Error),
    #[error("parse error: {0}")]
    Parse(String),
    #[error(transparent)]
    Identity(#[from] aid_identity::IdentityError),
}

pub type Result<T> = std::result::Result<T, NetError>;
