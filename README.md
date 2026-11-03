# GasAgencyManager (AgencyDB)

A Windows desktop ERP for an **LPG cylinder distributorship**, built with **C# / WPF / .NET 10** and a local **SQLite** database (`AgencyDB.db`). It covers the daily operations of a gas agency: warehouse stock, IOCL plant purchases, morning upliftment and evening return of cylinders by delivery vehicles, driver/vehicle cash settlement, commercial customer ledgers, and an auditable transaction log.

> This is a sanitized public copy of a private production project. Customer names, credentials and business data have been removed or replaced with placeholders.

## Features (as implemented in the code)

| Area | What it does |
|---|---|
| **Login & roles** | Username/password login (`LoginWindow`), salted SHA-256 password hashes, `Owner` and `Manager` roles. Owner-only Admin tab. |
| **Admin** | Manage warehouses, drivers and vehicles. All operational tabs stay disabled until at least one warehouse exists. |
| **Master Stock Update** | Per-warehouse filled/empty stock per cylinder type (`StockMaster`). |
| **IOCL Plant Purchase** | Bulk entry of filled cylinders received from the plant, with empty returns deducted. |
| **Morning Out (upliftment)** | Records cylinders loaded onto a vehicle per driver/date and deducts them from warehouse stock (`MorningUplifts`). |
| **Evening In** | Records empties/unsold filled cylinders returned, adds them back to stock. |
| **Accounting / settlement** | Per driver + vehicle + date reconciliation of expected refills vs. entered refills and cash collected (`DriverVehicleSettlements`, `AccountingSettlements`). |
| **Commercial Ledger** | Commercial (19 kg) customers, CSV import from an IOCL customer-register export, ledger of payments, empties and filled cylinders. |
| **Stock Report** | Date-range transaction/stock reports. |
| **Audit trail** | Every stock movement is written to `TransactionLogs`; transactions can be reversed with a reason, user and timestamp instead of being deleted. |

## Architecture

```
App.xaml(.cs)          → startup, DB init, shows LoginWindow then MainWindow
LoginWindow.xaml(.cs)  → authentication via DatabaseService.Authenticate
MainWindow.xaml(.cs)   → tabbed UI (Admin, stock, plant, morning/evening, accounting, ledger, reports)
Data/
  DatabaseService.cs           → SQLite schema creation/migration + all data access (parameterized SQL)
  CommercialCustomerImporter.cs→ CSV parsing for customer-register imports
Models/                → POCOs / INotifyPropertyChanged entry models (StockItem, TransactionLog,
                         ProductEntry, AccountingEntry, CommercialCustomer, Driver, Vehicle, Warehouse, User …)
installer/             → Inno Setup script + PowerShell build script
```

The UI uses WPF data binding with observable model classes (`ProductEntry`, `AccountingEntry` implement `INotifyPropertyChanged`), with screen logic in code-behind and persistence isolated in `DatabaseService` (a lightweight MVVM-style separation rather than a full MVVM framework).

Tables: `Users`, `Warehouses`, `Drivers`, `Vehicles`, `StockMaster`, `TransactionLogs`, `MorningUplifts`, `DriverVehicleSettlements`, `AccountingSettlements`, `CommercialCustomers`, `CommercialLedgerEntries`, `AppConfig`, `AppSettings`.

## Tech stack

- C# / .NET 10 (`net10.0-windows`), WPF
- SQLite via `Microsoft.Data.Sqlite`
- Single-file, self-contained publish (win-x64)
- Inno Setup 6 installer

## Build & run

Requirements: Windows, [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build
dotnet run
```

On first run `AgencyDB.db` is created next to the executable and two accounts are seeded: `owner` and `manager`. Their initial passwords are read from the environment variables `GAM_OWNER_PASSWORD` and `GAM_MANAGER_PASSWORD` (falling back to the placeholder `CHANGE_ME`). Set them before first launch:

```powershell
$env:GAM_OWNER_PASSWORD = "<strong password>"
$env:GAM_MANAGER_PASSWORD = "<strong password>"
dotnet run
```

Single portable EXE: `create-single-exe.bat` (or `build-windows-exe.bat`).
Installer: `.\installer\build-installer.ps1` → `installer\dist\GasAgencyManager-Setup.exe`.

Note: branding assets (app icon/logo) are not included in this public copy; the project builds without them.

More detail: [`docs/`](docs/).

## Author

**Bhavik Lal**
- GitHub: [techbhavikgj/agent-failure-casebook](https://github.com/techbhavikgj/agent-failure-casebook)
- LinkedIn: [lalbhavik1994](https://www.linkedin.com/in/lalbhavik1994)
