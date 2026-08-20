# 🏭 Industrial IoT & Digital Twin Platform — Siemens S7-1200

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-Web_API_%26_SignalR-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-Time--Series_Persistence-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Entity Framework Core](https://img.shields.io/badge/EF_Core-10.0-512BD4?logo=dotnet&logoColor=white)](https://docs.microsoft.com/ef/core/)
[![Siemens S7-1200](https://img.shields.io/badge/Siemens_PLC-S7--1200_Profinet-00646E?logo=siemens&logoColor=white)](https://www.siemens.com/)
[![S7.Net+](https://img.shields.io/badge/S7.Net%2B-0.20.0-green)](https://github.com/S7NetPlus/s7netplus)
[![SignalR](https://img.shields.io/badge/SignalR-Zero_Polling_RealTime-blue)](https://dotnet.microsoft.com/apps/aspnet/signalr)
[![License](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Endüstriyel üretim hatları ve **Siemens S7-1200 PLC** cihazları için geliştirilmiş, yüksek güvenilirlikli, **gerçek zamanlı (SignalR)**, zaman serisi veritabanı (PostgreSQL EF Core) destekli ve **kural motorlu (Alarm Engine)** bir **Endüstri 4.0 Dijital İkiz (Digital Twin)** izleme ve karar destek platformu.

---

## 📸 Ekran Görüntüleri & Dijital İkiz Arayüzü

| Dijital İkiz & Canlı Telemetri | PLC Komut & Rol Yetkilendirme |
|:---:|:---:|
| ![Digital Twin Dashboard](IndustrialDataLogger/Dashboard/screenshot_dashboard.png) | ![PLC Control](IndustrialDataLogger/Dashboard/screenshot_control.png) |

---

## 🏛️ Mimari Blok Diyagramı

```mermaid
flowchart TD
    subgraph Saha_ve_Kontrol_Kati [Saha & PLC Katmanı]
        PLC[Siemens S7-1200 PLC\nIP: 192.168.0.1:102 / DB1]
        MockPLC[Mock Virtual PLC\nSimülasyon Motoru]
    end

    subgraph Backend_Kati [Backend / Worker / Engine (.NET 10)]
        PlcMgr[IPlcConnectionManager\nAuto-Reconnect & State Machine]
        Worker[Background Worker Service\n2s Periyodik Örnekleme]
        AlarmEng[IAlarmService / Kural Motoru\nEşik Denetimi & Auto-Resolve]
        DBContext[IndustrialDbContext\nEF Core 10.0]
        HubContext[IHubContext&lt;MonitoringHub&gt;\nSignalR Real-Time Push]
    end

    subgraph Veri_Kati [Veritabanı Katmanı]
        PG[(PostgreSQL IndustrialDataDB\nsensordata + alarmlogs)]
    end

    subgraph Istemci_Kati [Frontend / UI Katmanı]
        WebClient[Web Browser / Dashboard\n- Animasyonlu SVG Dijital İkiz\n- Chart.js Canlı & Geçmiş Grafikler\n- Canlı Alarm & Teşhis Paneli]
    end

    PLC -->|S7Comm Protocol| PlcMgr
    MockPLC -->|In-Memory| PlcMgr
    PlcMgr --> Worker
    Worker --> AlarmEng
    Worker -->|Asenkron Loglama| DBContext
    DBContext --> PG
    Worker -->|WebSocket PUSH| HubContext
    AlarmEng -->|Alarm State Event| HubContext
    HubContext -->|Zero-Polling PUSH| WebClient
```

---

## 🚀 Öne Çıkan Özellikler

### 1. PLC Bağlantı Sağlığı & Otomatik Toparlanma (Auto-Reconnect)
- **5 Kademeli Durum Modeli:** `Connected`, `Disconnected`, `Connecting`, `Disconnecting`, `Reconnecting`.
- **Otomatik Kurtarma:** PLC bağlantısı aniden koptuğunda veri akışını güvenle kilitler, `Reconnecting` durumuna geçer ve arka planda her 3 saniyede bir PLC'yi yoklayarak insan müdahalesine gerek kalmadan bağlantıyı otomatik ayağa kaldırır.
- **Thread-Safe Haberleşme:** Worker ve Controller arasındaki soket çakışmalarını önleyen `SemaphoreSlim` kilitleme mekanizması.

### 2. Zaman Serisi Veri Kalıcılığı (PostgreSQL + EF Core)
- **Time-Series İndeksleme:** `IX_sensordata_timestamp` B-Tree zaman indeksi sayesinde milyonlarca kayıt arasından milisaniyeler içinde geçmiş veri çekme.
- **Performans Optimizasyonu:** `AsNoTracking()` ve `Take/Skip` filtrelemeleri ile minimum bellek ayak izi.
- **Otomatik Analitik:** Min, Max, Ortalama sıcaklık/basınç ve makine çalışma oranını (`OEE`) dinamik hesaplayan API.

### 3. Sıfır Gecikmeli Canlı Yayın (SignalR WebSocket Push)
- HTTP Polling (`setInterval`) tamamen kaldırılmıştır.
- PLC'den yeni veri okunduğu anda sunucu veriyi tüm bağlı istemcilere **WebSocket (PUSH)** ile fırlatır.
- `withAutomaticReconnect` ile ağ dalgalanmalarında istemci tarafında kesintisiz izleme.

### 4. Endüstriyel Karar Destek & Alarm Kural Motoru (Fault Detection)
- **Eşik Değerleri:**
  - Sıcaklık > 85°C ➔ 🔴 `CRITICAL_TEMPERATURE`
  - Sıcaklık > 70°C ➔ 🟡 `HIGH_TEMPERATURE` (Warning)
  - Basınç > 9.0 bar ➔ 🔴 `CRITICAL_PRESSURE`
  - Basınç > 7.5 bar ➔ 🟡 `HIGH_PRESSURE` (Warning)
  - PLC Kopması ➔ 🔴 `PLC_CONNECTION_LOST`
- **Otomatik Çözülme (Auto-Resolve):** Parametreler normale döndüğünde alarm otomatik olarak `Resolved` statüsüne geçer.
- **Operatör Onaylama (Acknowledge):** Operatörün gördüğü alarmları onaylayabilmesi için REST API desteği.

### 5. Dijital İkiz (Digital Twin) Görselleştirme
- **Fiziksel Durum Temsili:** Makine çalıştığında dönen motor rotoru, durduğunda anında kilitlenen SVG animasyonu.
- **Termal Bölme:** Sıcaklığa göre maviden kehribara ve kırmızıya dinamik renk değiştiren ısı odası.
- **Pnömatik Akış:** Basınç hattı canlı partikül akış efekti.

---

## 📡 RESTful API & SignalR Spesifikasyonu

| Metot | Endpoint | Açıklama |
|---|---|---|
| `GET` | `/api/digitaltwin/state` | Tüm telemetri, bağlantı, istatistik ve alarmları içeren birleşik Dijital İkiz durumu. |
| `GET` | `/api/Sensor/latest` | En son okunan anlık sensör telemetrisi. |
| `GET` | `/api/Sensor/history` | Filtrelenebilir (`startDate`, `endDate`, `machineStatus`, `limit`, `skip`) geçmiş veriler. |
| `GET` | `/api/Sensor/history/stats` | Min, Max, Ortalama ve Makine Çalışma Oranı istatistik özeti. |
| `POST` | `/api/Sensor/connect` | PLC / Simülasyon bağlantısını başlatır. |
| `POST` | `/api/Sensor/disconnect` | PLC bağlantısını güvenli kapatır. |
| `POST` | `/api/Sensor/mode` | Simülasyon ile Gerçek PLC arasında dinamik mod değişimi yapar. |
| `GET` | `/api/plc/status` | PLC bağlantı sağlığı (`state`, `isConnected`). |
| `GET` | `/api/alarms/active` | Anlık aktif alarmların listesi. |
| `GET` | `/api/alarms/history` | Geçmiş alarm olay günlüğü. |
| `POST` | `/api/alarms/{id}/acknowledge` | Alarmı operatör tarafından onaylar. |
| `WS` | `/sensorHub` | SignalR WebSocket Hub kanalı (`ReceiveSensorData`, `ReceivePlcStatus`, `ReceiveActiveAlarms`). |

---

## 🛠️ Kurulum & Yerel Çalıştırma

### Gereksinimler
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [PostgreSQL](https://www.postgresql.org/) (veya Docker PostgreSQL)

### 1. Depoyu Klonlayın
```bash
git clone https://github.com/YasinnEnes/IndustrialDataLogger.git
cd IndustrialDataLogger
```

### 2. Veritabanı Bağlantı Ayarları
`IndustrialDataLogger/appsettings.json` dosyasında PostgreSQL bağlantı cümlenizi kontrol edin:
```json
"ConnectionStrings": {
  "PostgreSql": "Host=localhost;Port=5432;Database=IndustrialDataDB;Username=postgres;Password=1234"
}
```

### 3. Uygulamayı Başlatın
```bash
dotnet build
dotnet run --project IndustrialDataLogger
```

### 4. Tarayıcıda Açın
- **Dashboard:** [http://localhost:5000](http://localhost:5000)
- **Swagger API Docs:** [http://localhost:5000/swagger](http://localhost:5000/swagger)

---

## 💼 CV / Portföy Maddeleri (İş Başvuruları İçin)

> **Siemens S7-1200 PLC & .NET 10 Tabanlı Endüstriyel Dijital İkiz (Digital Twin) Platformu**
> - **Endüstriyel Haberleşme:** Siemens S7-1200 PLC ile S7Comm/Profinet üzerinden thread-safe (`SemaphoreSlim`) veri okuma ve çift yönlü komut iletimi sağlayan haberleşme sürücüsü geliştirildi.
> - **Yüksek Erişilebilirlik (Auto-Reconnect):** Sahadaki ağ kesintilerine karşı otomatik toparlanan (Self-Healing) durum makinesi (State Machine) tasarlandı.
> - **Zaman Serisi & Performans:** PostgreSQL ve EF Core üzerinde B-Tree zaman indeksleri ve `AsNoTracking` optimizasyonlarıyla yüksek hacimli telemetri kayıt ve analitik sorgu altyapısı kuruldu.
> - **Gerçek Zamanlı Mimari:** ASP.NET Core SignalR ile HTTP polling yükü ortadan kaldırılarak sıfır gecikmeli WebSocket veri yayını sağlandı.
> - **Kural Motoru & Karar Destek:** Kritik sıcaklık, basınç ve bağlantı kesintilerine karşı otomatik alarm üreten ve çözen (Auto-Resolve) endüstriyel kural motoru geliştirildi.
> - **Dijital İkiz Arayüzü:** Sahadaki makinenin mekanik ve termal durumunu yansıtan canlı animasyonlu SVG Dijital İkiz paneli inşa edildi.
