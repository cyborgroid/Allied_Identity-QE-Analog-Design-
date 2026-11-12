//! 권한(Capability) 토큰.
//!
//! 비밀번호 대신, 기기 키로 서명된 Grant 가 "문을 돌릴 권한"이다.
//! `net.*` 같은 와일드카드로 하위 권한을 묶어서 부여할 수 있다.

use base64::{engine::general_purpose::STANDARD as B64, Engine};
use ed25519_dalek::{Signature, VerifyingKey};
use serde::{Deserialize, Serialize};
use std::fmt;
use std::fs;
use std::path::PathBuf;
use std::str::FromStr;

use crate::{io_err, parse_public_key, verify, DeviceIdentity, IdentityError, Result};

/// 표준 권한 목록 (문서화용; 임의의 점-구분 이름도 허용).
pub const STANDARD_CAPABILITIES: &[(&str, &str)] = &[
    ("call", "AlliedID 번호로 음성/영상 통화"),
    ("net.vpn", "국경 없는 오버레이 VPN 연결"),
    ("net.wifi", "주변 Wi-Fi 탐색 및 접속"),
    ("net.location", "Wi-Fi/GPS 위치 추정"),
    ("apps.install", "앱(데비안/Flatpak/Android) 설치"),
    ("desktop.edit", "블록 코딩으로 바탕화면 조립"),
    ("system.admin", "시스템 관리 (sudo 대체)"),
];

#[derive(Clone, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(try_from = "String", into = "String")]
pub struct Capability(String);

impl Capability {
    pub fn as_str(&self) -> &str {
        &self.0
    }

    /// `self` 권한이 `other` 를 포함하는가. `*` 는 전부, `net.*` 는 `net.vpn` 등.
    pub fn covers(&self, other: &Capability) -> bool {
        if self.0 == "*" || self.0 == other.0 {
            return true;
        }
        match self.0.strip_suffix(".*") {
            Some(prefix) => other.0.starts_with(prefix) && other.0[prefix.len()..].starts_with('.'),
            None => false,
        }
    }
}

impl FromStr for Capability {
    type Err = IdentityError;
    fn from_str(s: &str) -> Result<Self> {
        let ok = s == "*"
            || (!s.is_empty()
                && s.split('.').enumerate().all(|(i, part)| {
                    let last = i == s.split('.').count() - 1;
                    (last && part == "*") || (!part.is_empty() && part.chars().all(|c| c.is_ascii_lowercase() || c.is_ascii_digit() || c == '-'))
                }));
        if ok {
            Ok(Self(s.to_string()))
        } else {
            Err(IdentityError::Malformed(format!("invalid capability '{s}'")))
        }
    }
}

impl TryFrom<String> for Capability {
    type Error = IdentityError;
    fn try_from(s: String) -> Result<Self> {
        s.parse()
    }
}
impl From<Capability> for String {
    fn from(c: Capability) -> String {
        c.0
    }
}
impl fmt::Display for Capability {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

pub(crate) fn now() -> u64 {
    std::time::SystemTime::now().duration_since(std::time::UNIX_EPOCH).map(|d| d.as_secs()).unwrap_or(0)
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Grant {
    pub capability: Capability,
    /// 발급 기기 공개키 (base64)
    pub issuer: String,
    /// 권한을 받는 기기 공개키 (base64)
    pub subject: String,
    pub issued_at: u64,
    pub expires_at: Option<u64>,
    /// Ed25519 서명 (base64)
    pub signature: String,
}

impl Grant {
    pub(crate) fn issue(id: &DeviceIdentity, capability: Capability, subject: &VerifyingKey, ttl: Option<u64>) -> Self {
        let issued_at = now();
        let mut g = Grant {
            capability,
            issuer: id.public_key_b64(),
            subject: B64.encode(subject.to_bytes()),
            issued_at,
            expires_at: ttl.map(|t| issued_at + t),
            signature: String::new(),
        };
        g.signature = B64.encode(id.sign(&g.signed_bytes()).to_bytes());
        g
    }

    fn signed_bytes(&self) -> Vec<u8> {
        format!(
            "alliedid/grant/v1\n{}\n{}\n{}\n{}\n{}",
            self.capability,
            self.issuer,
            self.subject,
            self.issued_at,
            self.expires_at.map(|e| e.to_string()).unwrap_or_default()
        )
        .into_bytes()
    }

    pub fn subject_key(&self) -> Result<VerifyingKey> {
        parse_public_key(&self.subject)
    }

    pub fn issuer_key(&self) -> Result<VerifyingKey> {
        parse_public_key(&self.issuer)
    }

    /// 서명과 만료를 `t` 시점 기준으로 검증.
    pub fn verify_at(&self, t: u64) -> Result<()> {
        let sig_bytes = B64.decode(&self.signature).map_err(|e| IdentityError::Malformed(e.to_string()))?;
        let sig = Signature::from_slice(&sig_bytes).map_err(|_| IdentityError::BadSignature)?;
        verify(&self.issuer_key()?, &self.signed_bytes(), &sig)?;
        match self.expires_at {
            Some(e) if t >= e => Err(IdentityError::Malformed("grant expired".into())),
            _ => Ok(()),
        }
    }

    /// 파일명으로 쓰기 좋은 짧은 ID.
    pub fn id(&self) -> String {
        use sha2::{Digest, Sha256};
        crate::hex(&Sha256::digest(self.signature.as_bytes())[..6])
    }
}

/// `<state>/grants/*.json` 에 저장되는 권한 모음.
pub struct GrantStore {
    dir: PathBuf,
}

impl GrantStore {
    pub fn new(dir: PathBuf) -> Self {
        Self { dir }
    }

    pub fn save(&self, g: &Grant) -> Result<PathBuf> {
        fs::create_dir_all(&self.dir).map_err(io_err(&self.dir))?;
        let path = self.dir.join(format!("{}-{}.json", g.capability.as_str().replace('*', "all"), g.id()));
        fs::write(&path, serde_json::to_string_pretty(g).unwrap()).map_err(io_err(&path))?;
        Ok(path)
    }

    pub fn list(&self) -> Result<Vec<Grant>> {
        let mut out = Vec::new();
        let rd = match fs::read_dir(&self.dir) {
            Ok(rd) => rd,
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => return Ok(out),
            Err(e) => return Err(IdentityError::Io { path: self.dir.clone(), source: e }),
        };
        for entry in rd.flatten() {
            let p = entry.path();
            if p.extension().and_then(|e| e.to_str()) != Some("json") {
                continue;
            }
            if let Ok(g) = fs::read_to_string(&p).map_err(|_| ()).and_then(|s| serde_json::from_str::<Grant>(&s).map_err(|_| ())) {
                out.push(g);
            }
        }
        out.sort_by(|a, b| a.capability.as_str().cmp(b.capability.as_str()));
        Ok(out)
    }

    /// 해당 권한을 정확히 가진 grant 파일을 삭제(회수).
    pub fn revoke(&self, cap: &Capability) -> Result<usize> {
        let mut n = 0;
        for g in self.list()? {
            if &g.capability == cap {
                let path = self.dir.join(format!("{}-{}.json", g.capability.as_str().replace('*', "all"), g.id()));
                if fs::remove_file(&path).is_ok() {
                    n += 1;
                }
            }
        }
        Ok(n)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn wildcard_coverage() {
        let c = |s: &str| s.parse::<Capability>().unwrap();
        assert!(c("*").covers(&c("system.admin")));
        assert!(c("net.*").covers(&c("net.vpn")));
        assert!(!c("net.*").covers(&c("network.vpn")));
        assert!(!c("net.vpn").covers(&c("net.wifi")));
        assert!("Net.VPN".parse::<Capability>().is_err());
        assert!("a..b".parse::<Capability>().is_err());
        assert!("a.*.b".parse::<Capability>().is_err());
    }
}
