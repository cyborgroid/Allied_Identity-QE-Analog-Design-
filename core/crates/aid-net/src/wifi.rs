//! Wi-Fi 탐색. NetworkManager 의 terse 출력(`nmcli -t`)을 파싱한다.
//! terse 모드에서는 필드 안의 `:` 와 `\` 가 `\:` / `\\` 로 이스케이프된다.

use serde::{Deserialize, Serialize};
use std::process::Command;

use crate::{NetError, Result};

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct AccessPoint {
    pub ssid: String,
    pub bssid: String,
    /// 0–100 (%), NetworkManager 기준
    pub signal: u8,
    /// MHz
    pub freq_mhz: u32,
    pub security: String,
    pub in_use: bool,
}

impl AccessPoint {
    /// NM 의 신호 % 를 대략적인 dBm 으로 환산 (NM: dBm = %/2 - 100 의 역).
    pub fn rssi_dbm(&self) -> f64 {
        self.signal as f64 / 2.0 - 100.0
    }
}

pub const NMCLI_FIELDS: &str = "IN-USE,SSID,BSSID,SIGNAL,FREQ,SECURITY";

/// terse 한 줄을 이스케이프를 고려해 필드로 분리.
pub fn split_terse(line: &str) -> Vec<String> {
    let mut fields = vec![String::new()];
    let mut chars = line.chars();
    while let Some(c) = chars.next() {
        match c {
            '\\' => {
                if let Some(n) = chars.next() {
                    fields.last_mut().unwrap().push(n);
                }
            }
            ':' => fields.push(String::new()),
            _ => fields.last_mut().unwrap().push(c),
        }
    }
    fields
}

/// `nmcli -t -f IN-USE,SSID,BSSID,SIGNAL,FREQ,SECURITY dev wifi list` 출력 파싱.
pub fn parse_nmcli(output: &str) -> Vec<AccessPoint> {
    output
        .lines()
        .filter(|l| !l.trim().is_empty())
        .filter_map(|l| {
            let f = split_terse(l);
            if f.len() < 6 {
                return None;
            }
            Some(AccessPoint {
                in_use: f[0].trim() == "*",
                ssid: f[1].clone(),
                bssid: f[2].to_ascii_lowercase(),
                signal: f[3].trim().parse().ok()?,
                freq_mhz: f[4].split_whitespace().next()?.parse().ok()?,
                security: f[5].clone(),
            })
        })
        .collect()
}

/// 실제 스캔. `rescan` 이 true 면 새로 스캔을 요청한다(권한 필요할 수 있음).
pub fn scan(rescan: bool) -> Result<Vec<AccessPoint>> {
    let out = Command::new("nmcli")
        .args(["-t", "-f", NMCLI_FIELDS, "dev", "wifi", "list", "--rescan", if rescan { "yes" } else { "auto" }])
        .output()
        .map_err(|e| NetError::Command { cmd: "nmcli".into(), msg: e.to_string() })?;
    if !out.status.success() {
        return Err(NetError::Command { cmd: "nmcli".into(), msg: String::from_utf8_lossy(&out.stderr).trim().to_string() });
    }
    let mut aps = parse_nmcli(&String::from_utf8_lossy(&out.stdout));
    aps.sort_by_key(|a| std::cmp::Reverse(a.signal));
    Ok(aps)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_escaped_terse_output() {
        let out = "*:Home\\:5G:AA\\:BB\\:CC\\:DD\\:EE\\:FF:82:5180 MHz:WPA2\n \
                   :Cafe Free:11\\:22\\:33\\:44\\:55\\:66:40:2437 MHz:\n";
        let aps = parse_nmcli(out);
        assert_eq!(aps.len(), 2);
        assert_eq!(aps[0].ssid, "Home:5G");
        assert_eq!(aps[0].bssid, "aa:bb:cc:dd:ee:ff");
        assert!(aps[0].in_use);
        assert_eq!(aps[0].freq_mhz, 5180);
        assert_eq!(aps[1].security, "");
        assert!((aps[1].rssi_dbm() + 80.0).abs() < 1e-9);
    }
}
