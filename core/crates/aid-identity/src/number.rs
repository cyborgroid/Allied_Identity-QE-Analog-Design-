//! AlliedID 번호: 국가번호(`+82` 같은 접두사)가 없는 12자리 기기 번호.
//!
//! `SHA-256("alliedid/number/v1" || pubkey)` 의 앞 8바이트를 11자리로 줄이고
//! Luhn 체크 숫자를 붙인다. 표시 형식: `AID 4821-0937-5512`.

use ed25519_dalek::VerifyingKey;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::fmt;
use std::str::FromStr;

use crate::IdentityError;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(try_from = "String", into = "String")]
pub struct AidNumber([u8; 12]);

impl AidNumber {
    pub fn from_public_key(key: &VerifyingKey) -> Self {
        let mut h = Sha256::new();
        h.update(b"alliedid/number/v1");
        h.update(key.as_bytes());
        let d = h.finalize();
        let n = u64::from_be_bytes(d[..8].try_into().unwrap()) % 100_000_000_000;
        let mut digits = [0u8; 12];
        let body = format!("{n:011}");
        for (i, c) in body.bytes().enumerate() {
            digits[i] = c - b'0';
        }
        digits[11] = luhn_check_digit(&digits[..11]);
        Self(digits)
    }

    /// 다이얼용 12자리 숫자열.
    pub fn digits(&self) -> String {
        self.0.iter().map(|d| char::from(b'0' + d)).collect()
    }

    /// 사람이 읽는 형식 `AID 4821-0937-5512`.
    pub fn display(&self) -> String {
        let d = self.digits();
        format!("AID {}-{}-{}", &d[0..4], &d[4..8], &d[8..12])
    }
}

fn luhn_check_digit(body: &[u8]) -> u8 {
    let sum: u32 = body
        .iter()
        .rev()
        .enumerate()
        .map(|(i, &d)| {
            let d = d as u32;
            if i % 2 == 0 {
                let x = d * 2;
                if x > 9 { x - 9 } else { x }
            } else {
                d
            }
        })
        .sum();
    ((10 - (sum % 10)) % 10) as u8
}

impl FromStr for AidNumber {
    type Err = IdentityError;
    /// `AID 4821-0937-5512`, `4821 0937 5512`, `482109375512` 모두 허용.
    fn from_str(s: &str) -> Result<Self, Self::Err> {
        let t = s.trim();
        let t = t.strip_prefix("AID").or_else(|| t.strip_prefix("aid")).unwrap_or(t);
        let digits: Vec<u8> = t.chars().filter(|c| !matches!(c, ' ' | '-' | '.')).map(|c| c.to_digit(10).map(|d| d as u8)).collect::<Option<_>>()
            .ok_or_else(|| IdentityError::InvalidNumber(s.into()))?;
        if t.contains('+') || digits.len() != 12 {
            return Err(IdentityError::InvalidNumber(s.into()));
        }
        if luhn_check_digit(&digits[..11]) != digits[11] {
            return Err(IdentityError::InvalidNumber(format!("{s} (check digit)")));
        }
        Ok(Self(digits.try_into().unwrap()))
    }
}

impl TryFrom<String> for AidNumber {
    type Error = IdentityError;
    fn try_from(s: String) -> Result<Self, Self::Error> {
        s.parse()
    }
}

impl From<AidNumber> for String {
    fn from(n: AidNumber) -> String {
        n.digits()
    }
}

impl fmt::Display for AidNumber {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.display())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use ed25519_dalek::SigningKey;

    #[test]
    fn number_is_stable_and_parses_back() {
        let k = SigningKey::from_bytes(&[7u8; 32]).verifying_key();
        let n = AidNumber::from_public_key(&k);
        assert_eq!(n, AidNumber::from_public_key(&k));
        assert_eq!(n.digits().len(), 12);
        assert_eq!(n.display().parse::<AidNumber>().unwrap(), n);
        assert_eq!(n.digits().parse::<AidNumber>().unwrap(), n);
    }

    #[test]
    fn rejects_country_codes_and_typos() {
        let k = SigningKey::from_bytes(&[7u8; 32]).verifying_key();
        let d = AidNumber::from_public_key(&k).digits();
        assert!(format!("+82{}", &d[2..]).parse::<AidNumber>().is_err());
        let mut typo: Vec<char> = d.chars().collect();
        typo[3] = if typo[3] == '1' { '2' } else { '1' };
        assert!(typo.iter().collect::<String>().parse::<AidNumber>().is_err());
    }
}
