//! 위치 추정: "WIFI 가 GPS 가 된다".
//!
//! * Wi-Fi: 알려진 AP(BSSID → 좌표) DB 와 신호 세기로 가중 중심점을 구한다.
//!   DB 는 로컬 JSON 이며, 기기들이 GPS 고정 시 주변 AP 를 학습해 채워 간다
//!   (`learn`). 외부 위치 서비스에 의존하지 않는다.
//! * GPS: gpsd(TCP 2947) 의 TPV 메시지를 읽는다.
//! * 융합: 두 추정치를 정확도(분산)의 역수로 가중 평균.

use serde::{Deserialize, Serialize};
use std::collections::HashMap;
use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::path::Path;
use std::time::{Duration, Instant};

use crate::wifi::AccessPoint;
use crate::{NetError, Result};

#[derive(Clone, Copy, Debug, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Source {
    Wifi,
    Gps,
    Fused,
}

#[derive(Clone, Debug, PartialEq, Serialize, Deserialize)]
pub struct Fix {
    pub lat: f64,
    pub lon: f64,
    /// 1-sigma 수평 정확도 (m)
    pub accuracy_m: f64,
    pub source: Source,
}

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct KnownAp {
    pub lat: f64,
    pub lon: f64,
    /// 이 위치를 관측한 횟수 (학습 가중치)
    #[serde(default = "one")]
    pub samples: u32,
}
fn one() -> u32 {
    1
}

/// BSSID(소문자) → 좌표
#[derive(Clone, Debug, Default, Serialize, Deserialize)]
pub struct ApDatabase {
    pub aps: HashMap<String, KnownAp>,
}

impl ApDatabase {
    pub fn load(path: &Path) -> Result<Self> {
        match std::fs::read_to_string(path) {
            Ok(s) => serde_json::from_str(&s).map_err(|e| NetError::Parse(e.to_string())),
            Err(e) if e.kind() == std::io::ErrorKind::NotFound => Ok(Self::default()),
            Err(e) => Err(e.into()),
        }
    }

    pub fn save(&self, path: &Path) -> Result<()> {
        if let Some(p) = path.parent() {
            std::fs::create_dir_all(p)?;
        }
        std::fs::write(path, serde_json::to_string_pretty(self).unwrap())?;
        Ok(())
    }

    /// GPS 고정이 있을 때 주변 AP 위치를 학습 (이동 평균).
    pub fn learn(&mut self, gps: &Fix, scan: &[AccessPoint]) -> usize {
        for ap in scan {
            let e = self.aps.entry(ap.bssid.clone()).or_insert(KnownAp { lat: gps.lat, lon: gps.lon, samples: 0 });
            let n = e.samples as f64;
            e.lat = (e.lat * n + gps.lat) / (n + 1.0);
            e.lon = (e.lon * n + gps.lon) / (n + 1.0);
            e.samples = e.samples.saturating_add(1);
        }
        scan.len()
    }
}

/// 경로손실 모델로 RSSI → 대략 거리(m). 실내 n≈3, 1m 기준 -40dBm.
fn rssi_to_distance_m(rssi_dbm: f64) -> f64 {
    10f64.powf((-40.0 - rssi_dbm) / (10.0 * 3.0)).clamp(1.0, 300.0)
}

/// Wi-Fi 만으로 추정. DB 에 있는 AP 가 하나도 없으면 None.
pub fn estimate_wifi(scan: &[AccessPoint], db: &ApDatabase) -> Option<Fix> {
    let mut wsum = 0.0;
    let (mut lat, mut lon, mut dist) = (0.0, 0.0, 0.0);
    for ap in scan {
        let Some(k) = db.aps.get(&ap.bssid) else { continue };
        let d = rssi_to_distance_m(ap.rssi_dbm());
        let w = 1.0 / (d * d);
        lat += k.lat * w;
        lon += k.lon * w;
        dist += d * w;
        wsum += w;
    }
    (wsum > 0.0).then(|| Fix { lat: lat / wsum, lon: lon / wsum, accuracy_m: (dist / wsum).max(5.0), source: Source::Wifi })
}

/// 두 추정치를 분산 역수로 가중 평균.
pub fn fuse(a: Option<Fix>, b: Option<Fix>) -> Option<Fix> {
    match (a, b) {
        (None, x) | (x, None) => x,
        (Some(a), Some(b)) => {
            let wa = 1.0 / (a.accuracy_m * a.accuracy_m);
            let wb = 1.0 / (b.accuracy_m * b.accuracy_m);
            let w = wa + wb;
            Some(Fix {
                lat: (a.lat * wa + b.lat * wb) / w,
                lon: (a.lon * wa + b.lon * wb) / w,
                accuracy_m: (1.0 / w).sqrt(),
                source: Source::Fused,
            })
        }
    }
}

/// gpsd 의 JSON 한 줄(TPV)을 Fix 로. mode < 2 (no fix) 면 None.
pub fn parse_gpsd_tpv(line: &str) -> Option<Fix> {
    let v: serde_json::Value = serde_json::from_str(line).ok()?;
    if v.get("class")?.as_str()? != "TPV" || v.get("mode")?.as_u64()? < 2 {
        return None;
    }
    let acc = match (v.get("epx").and_then(|x| x.as_f64()), v.get("epy").and_then(|x| x.as_f64())) {
        (Some(x), Some(y)) => (x * x + y * y).sqrt() / 2f64.sqrt(),
        _ => v.get("eph").and_then(|x| x.as_f64()).unwrap_or(15.0),
    };
    Some(Fix { lat: v.get("lat")?.as_f64()?, lon: v.get("lon")?.as_f64()?, accuracy_m: acc.max(1.0), source: Source::Gps })
}

/// gpsd 에 접속해 `timeout` 내 첫 유효 TPV 를 읽는다. gpsd 가 없으면 Ok(None).
pub fn read_gpsd(addr: &str, timeout: Duration) -> Result<Option<Fix>> {
    let Ok(mut s) = TcpStream::connect(addr) else { return Ok(None) };
    s.set_read_timeout(Some(Duration::from_millis(500)))?;
    s.write_all(b"?WATCH={\"enable\":true,\"json\":true};\n")?;
    let start = Instant::now();
    let mut r = BufReader::new(s);
    let mut line = String::new();
    while start.elapsed() < timeout {
        line.clear();
        match r.read_line(&mut line) {
            Ok(0) => break,
            Ok(_) => {
                if let Some(f) = parse_gpsd_tpv(&line) {
                    return Ok(Some(f));
                }
            }
            Err(e) if matches!(e.kind(), std::io::ErrorKind::WouldBlock | std::io::ErrorKind::TimedOut) => continue,
            Err(e) => return Err(e.into()),
        }
    }
    Ok(None)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn ap(bssid: &str, signal: u8) -> AccessPoint {
        AccessPoint { ssid: "x".into(), bssid: bssid.into(), signal, freq_mhz: 2412, security: String::new(), in_use: false }
    }

    #[test]
    fn wifi_estimate_leans_to_stronger_ap() {
        let mut db = ApDatabase::default();
        db.aps.insert("a".into(), KnownAp { lat: 37.0, lon: 127.0, samples: 1 });
        db.aps.insert("b".into(), KnownAp { lat: 37.001, lon: 127.0, samples: 1 });
        let f = estimate_wifi(&[ap("a", 90), ap("b", 30), ap("unknown", 99)], &db).unwrap();
        assert!(f.lat < 37.0005, "closer to strong AP a: {}", f.lat);
        assert!(estimate_wifi(&[ap("zzz", 50)], &db).is_none());
    }

    #[test]
    fn fusion_prefers_more_accurate_source() {
        let wifi = Fix { lat: 37.0, lon: 127.0, accuracy_m: 50.0, source: Source::Wifi };
        let gps = Fix { lat: 37.001, lon: 127.0, accuracy_m: 5.0, source: Source::Gps };
        let f = fuse(Some(wifi), Some(gps)).unwrap();
        assert!(f.lat > 37.0009);
        assert!(f.accuracy_m < 5.0);
        assert_eq!(f.source, Source::Fused);
    }

    #[test]
    fn learning_then_locating() {
        let mut db = ApDatabase::default();
        let gps = Fix { lat: 35.1, lon: 129.0, accuracy_m: 4.0, source: Source::Gps };
        db.learn(&gps, &[ap("c", 70)]);
        let f = estimate_wifi(&[ap("c", 70)], &db).unwrap();
        assert!((f.lat - 35.1).abs() < 1e-9);
    }

    #[test]
    fn parses_gpsd_tpv() {
        let l = r#"{"class":"TPV","mode":3,"lat":37.5665,"lon":126.978,"epx":3.0,"epy":4.0}"#;
        let f = parse_gpsd_tpv(l).unwrap();
        assert!((f.lat - 37.5665).abs() < 1e-9);
        assert!(parse_gpsd_tpv(r#"{"class":"TPV","mode":1}"#).is_none());
        assert!(parse_gpsd_tpv(r#"{"class":"SKY"}"#).is_none());
    }
}
