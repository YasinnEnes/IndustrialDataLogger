# 🏭 Industrial IoT & Digital Twin Platform — Siemens S7-1200

<div align="center">

![CI Build](https://github.com/YasinnEnes/IndustrialDataLogger/actions/workflows/ci.yml/badge.svg)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-13.0-239120?logo=csharp&logoColor=white)](https://docs.microsoft.com/dotnet/csharp/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Web_API_%26_SignalR-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Time--Series_Persistence-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Docker](https://img.shields.io/badge/Docker-Compose_Ready-2496ED?logo=docker&logoColor=white)](https://www.docker.com/)
[![xUnit](https://img.shields.io/badge/xUnit-15%2F15_Passed-brightgreen)](https://xunit.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

<p align="center">
  <b>Endüstri 4.0, SCADA ve MES standartlarında; Siemens S7-1200 PLC ile çift yönlü haberleşen, kural tabanlı Sağlık Skoru (Health Score) ve OEE (Overall Equipment Effectiveness) motoruna sahip, JWT/RBAC güvenliği alınmış ve Docker ile izole edilmiş kurumsal Dijital İkiz (Digital Twin) platformu.</b>
</p>

[Özellikler](#-temel-özellikler) • [Mimari](#-sistem-mimarisi) • [OEE & Sağlık Skoru](#-sağlık-skoru--oee-motoru) • [Güvenlik & RBAC](#-güvenlik-ve-gözlemlenebilirlik) • [Docker Kurulumu](#-docker-ile-hızlı-başlangıç) • [Test Paketi](#-test-paketi--doğrulama) • [CV & Portföy](#-cv--portföy-özeti)

</div>

---

## 🌟 Temel Özellikler

- **🔌 Siemens S7-1200 Profinet/S7Comm Sürücüsü:** Gerçek donanım ve 5 farklı arıza senaryosu içeren sanal simülasyon motoru arasında kesintisiz geçiş (Zero Downtime).
- **🛡️ Dayanıklı Bağlantı Durum Makinesi (Auto-Reconnect):** Sahadaki ağ kopmalarına karşı **Exponential Backoff** (2s -> 4s -> 8s -> 16s -> 30s) stratejisiyle otomatik toparlanan durum makinesi (`Connected`, `Reconnecting`, `Connecting`, `Disconnected`).
- **⚡ Sıfır Gecikmeli Telemetri (SignalR WebSockets):** Geleneksel HTTP polling yükünü ortadan kaldıran, yeni PLC verisi okunduğunda istemcilere anında push yapan WebSocket mimarisi.
- **📊 PostgreSQL Zaman Serisi & EF Core 10:** `sensordata`, `alarmlogs` ve `systemeventlogs` tabloları üzerinde B-Tree zaman indeksli yüksek performanslı veri kalıcılığı.
- **🚨 Akıllı Alarm & Olay Yaşam Döngüsü (Alarm Lifecycle):** `NORMAL` ➔ `TRIGGERED` ➔ `ACTIVE` ➔ `ACKNOWLEDGED` ➔ `RESOLVED` akışı ve operatör onaylama sistemi.
- **🩺 Kural Tabanlı Sağlık Skoru (Health Scoring Engine):** Sıcaklık (%25), Basınç (%20), Bağlantı (%20) ve Alarmlar (%35) ağırlıklarıyla 0-100% arası ağırlıklı sağlık puanı (`HEALTHY`, `WARNING`, `DEGRADED`, `CRITICAL`).
- **🏆 OEE & Üretim Verimliliği Motoru:** Kullanılabilirlik (Availability), Performans (Performance) ve Kalite (Quality) bileşenleriyle dünya standardında OEE ve Çevrim Sayacı (Cycle Count).
- **🔐 JWT Kimlik Doğrulama & RBAC:** `Admin`, `Programmer`, `Operator` rolleri, 401/403 koruması ve Swagger Bearer Auth desteği.
- **🔭 Gözlemlenebilirlik (Observability & Health Checks):** `/health` endpoint'i üzerinden API, PostgreSQL, PLC ve Sistem RAM/Uptime izleme.
- **🐳 Docker & Docker Compose:** Backend ve PostgreSQL servislerini tek komutla (`docker compose up`) ayağa kaldıran konteyner altyapısı.

---

## 🏛️ Sistem Mimarisi

```mermaid
flowchart TD
    subgraph Saha_Kati [1. Donanım & Simülasyon Katmanı]
        PLC["Siemens S7-1200 PLC\n(IP: 192.168.0.1:102 / DB1)"]
        MockPLC["Simulation Engine\n(Normal, Overheating, HighPressure, MachineStop, PlcDisconnect)"]
    end

    subgraph Backend_Kati [2. Backend & Engine Katmanı (.NET 10)]
        PlcMgr["IPlcConnectionManager\n(Exponential Backoff & State Machine)"]
        Worker["Background Worker Service\n(2s Periyodik Örnekleme)"]
        AlarmEng["IAlarmService\n(Eşik Denetimi & Lifecycle)"]
        EventEng["IEventLogService\n(Audit & System Event Logs)"]
        TwinEng["IDigitalTwinService\n(Health Scoring & OEE Engine)"]
        AuthEng["IJwtTokenService\n(JWT Bearer & RBAC)"]
        DBContext["IndustrialDbContext\n(EF Core 10.0 Npgsql)"]
        HubContext["IHubContext&lt;MonitoringHub&gt;\n(SignalR WebSocket)"]
    end

    subgraph Veri_Kati [3. Kalıcılık Katmanı]
        PG[("PostgreSQL 16\nsensordata + alarmlogs + systemeventlogs")]
    end

    subgraph Istemci_Kati [4. Operasyonel Arayüz]
        WebClient["Industrial Operations Dashboard\n- SVG Dijital İkiz Şeması\n- Canlı Telemetri & OEE Paneli\n- Aktif Alarmlar & Event Timeline\n- 280px Chart.js Grafikleri"]
    end

    PLC -->|S7Comm Protocol| PlcMgr
    MockPLC -->|In-Memory| PlcMgr
    PlcMgr --> Worker
    Worker --> AlarmEng
    Worker --> EventEng
    Worker --> TwinEng
    Worker -->|Asenkron DB Log| DBContext
    DBContext --> PG
    Worker -->|Canlı PUSH| HubContext
    AlarmEng -->|Alarm Event| HubContext
    EventEng -->|System Event| HubContext
    TwinEng -->|Twin State| HubContext
    HubContext -->|Zero-Polling WebSocket| WebClient
```

---

## 🩺 Sağlık Skoru & OEE Motoru

### 1. Kural Tabanlı Sağlık Skoru (Health Score)
$$\text{Health Score} = S_{\text{Sıcaklık}} (25p) + S_{\text{Basınç}} (20p) + S_{\text{Bağlantı}} (20p) + S_{\text{Alarm}} (35p)$$

| Skor Aralığı | Derece | Açıklama |
| :--- | :--- | :--- |
| **85% – 100%** | `HEALTHY` | Tüm parametreler ve bağlantı optimum seviyede. |
| **65% – 84%** | `WARNING` | Uyarı seviyesinde sıcaklık/basınç veya alarm mevcut. |
| **40% – 64%** | `DEGRADED` | Birden fazla alarm veya bağlantı kesintisi. |
| **0% – 39%** | `CRITICAL` | Kritik sıcaklık/basınç eşiği aşıldı veya makine arızada. |

### 2. OEE (Overall Equipment Effectiveness)
$$\text{OEE} = \text{Availability} \times \text{Performance} \times \text{Quality}$$
- **Availability (Kullanılabilirlik):** Çalışma Süresi / Planlanan Üretim Süresi.
- **Performance (Performans):** Gerçek Üretim Hızı / İdeal Çevrim Hızı.
- **Quality (Kalite):** Sağlam Parça / Toplam Üretilen Parça (Fire Oranı Takibi).

---

## 🔐 Güvenlik ve Gözlemlenebilirlik

### 1. JWT & RBAC Rol Matrisi
| Rol | Kullanıcı | Yetkiler |
| :--- | :--- | :--- |
| **Admin** | `admin / admin123` | Tam yetki, PLC komut gönderimi, senaryo motoru kontrolü, sistem olayları. |
| **Programmer** | `programmer / prog123` | PLC Setpoint değiştirme, simülasyon modu geçişi, senaryo tetikleme. |
| **Operator** | `operator / op123` | Dashboard izleme, alarm onaylama (`Acknowledge`), grafik filtreleme. |

### 2. Gözlemlenebilirlik (`GET /health`)
```json
{
  "status": "Healthy",
  "responseTimeMs": 25,
  "uptime": "1d 4h 12m 30s",
  "components": {
    "api": { "status": "Healthy", "version": "1.0.0", "framework": ".NET 10.0", "memoryUsageMb": 132.4 },
    "database": { "status": "Healthy", "provider": "PostgreSQL (Npgsql)" },
    "plc": { "status": "Healthy", "connectionState": "Connected", "scenario": "Normal" },
    "digitalTwin": { "operationalStatus": "Running", "healthScore": 100, "healthGrade": "Healthy" }
  }
}
```

---

## 🐳 Docker ile Hızlı Başlangıç

Projeyi ve PostgreSQL veritabanını tek komutla ayağa kaldırmak için:

```bash
# Projeyi ve veritabanını Docker ile başlat
docker compose up -d

# Logları takip et
docker compose logs -f
```
Dashboard'a erişim: [**http://localhost:5000**](http://localhost:5000)

---

## 🧪 Test Paketi & Doğrulama

### 1. xUnit Birim Testlerini Çalıştırma
```bash
dotnet test
```
```
Toplam 1 test dosyası belirtilen desenle eşleşti.
Başarılı! - Başarısız: 0, Başarılı: 15, Atlanan: 0, Toplam: 15 (433 ms)
```

### 2. 7 Adımlı Uçtan Uca Demo Senaryosu
```bash
powershell -ExecutionPolicy Bypass -File ".\IndustrialDataLogger\scratch\full_demo_runner.ps1"
```
```
[STEP 1/7] JWT Authentication (Admin Login)... -> OK
[STEP 2/7] PLC Bağlantısı & Normal Senaryo... -> OK
[STEP 3/7] Aşırı Isınma (Overheating) Senaryosu... -> OK
[STEP 4/7] Aktif Alarmlar & Olay Günlüğü Kontrolü... -> OK (HIGH_TEMPERATURE Warning)
[STEP 5/7] Operatör Alarm Onaylama (Acknowledge)... -> OK
[STEP 6/7] Sıcaklık Düşürme & Alarm Çözümleme... -> OK
[STEP 7/7] PLC Kopması & Otomatik Yeniden Bağlanma... -> OK
================================================================================
   TÜM DEMO SENARYOLARI VE ENTEGRASYON ADIMLARI %100 BAŞARIYLA TAMAMLANDI!
================================================================================
```

---

## 💼 CV & Portföy Özeti

```markdown
- **Siemens S7-1200 Endüstriyel IoT & Dijital İkiz Platformu (.NET 10, C# 13, SignalR, PostgreSQL, Docker):**
  - Siemens S7-1200 PLC ile S7Comm/Profinet üzerinden çift yönlü haberleşen, ağ kopmalarına karşı **Exponential Backoff State Machine** mimarisine sahip dayanıklı arka plan servisi geliştirdi.
  - Sıcaklık, basınç, bağlantı ve alarm metriklerini ağırlıklandıran **Kural Tabanlı Sağlık Skoru (Health Scoring)** ve **OEE (Overall Equipment Effectiveness)** üretim verimliliği motorunu tasarladı.
  - Sıfır gecikmeli **SignalR WebSocket** veri boru hattı, **JWT & RBAC** yetkilendirmesi, **ASP.NET Core Health Checks** gözlemlenebilirlik altyapısı ve çok aşamalı **Docker Compose** ortamını kurdu.
  - xUnit ile 15 birim test ve 11 entegrasyon testi içeren otomatik test/demo paketini hayata geçirdi.
```

---

<div align="center">
  Geliştirici: <b>Yasin Enes</b> • <a href="https://github.com/YasinnEnes">GitHub Profil</a>
</div>
