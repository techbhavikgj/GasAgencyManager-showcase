using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using GasAgencyManager.Models;

namespace GasAgencyManager.Data
{
    public class DatabaseService
    {
        private const string BusinessDateKey = "BusinessDate";

        public static string DatabasePath =>
            Path.Combine(AppContext.BaseDirectory, "AgencyDB.db");

        public static string ConnectionString =>
            $"Data Source={DatabasePath}";

        private readonly string connectionString = ConnectionString;

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex) || hex.Length % 2 != 0) return Array.Empty<byte>();
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        private static (string SaltHex, string HashHex) HashPassword(string password, byte[] salt)
        {
            using var sha = SHA256.Create();
            var pwBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
            var combined = new byte[salt.Length + pwBytes.Length];
            Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
            Buffer.BlockCopy(pwBytes, 0, combined, salt.Length, pwBytes.Length);
            var hash = sha.ComputeHash(combined);
            return (ToHex(salt), ToHex(hash));
        }

        public List<Driver> GetDrivers()
        {
            var list = new List<Driver>();
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                var cmd = new SqliteCommand("SELECT Id, Name FROM Drivers", conn);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(new Driver { Id = r.GetInt32(0), Name = r.GetString(1) });
                }
            }
            return list;
        }

        public List<Vehicle> GetVehicles()
        {
            var list = new List<Vehicle>();
            using (var conn = new SqliteConnection(connectionString))
            {
                conn.Open();
                var cmd = new SqliteCommand("SELECT Id, VehicleNumber FROM Vehicles", conn);
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) list.Add(new Vehicle { Id = r.GetInt32(0), VehicleNumber = r.GetString(1) });
                }
            }
            return list;
        }

        public void SeedLogisticsData()
        {
            // No default warehouses, drivers, or vehicles — Owner configures these in the Admin tab.
        }

        public void InitializeDatabase()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();

                // Create Warehouses table
                command.CommandText = "CREATE TABLE IF NOT EXISTS Warehouses (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);";
                command.ExecuteNonQuery();

                // Create StockMaster table
                command.CommandText = "CREATE TABLE IF NOT EXISTS StockMaster (WarehouseId INTEGER, CylinderType TEXT, FilledQty INTEGER, EmptyQty INTEGER);";
                command.ExecuteNonQuery();

                // Create TransactionLogs table
                command.CommandText = "CREATE TABLE IF NOT EXISTS TransactionLogs (Id INTEGER PRIMARY KEY AUTOINCREMENT, WarehouseId INTEGER, CylinderType TEXT, FilledChange INTEGER, EmptyChange INTEGER, TransactionType TEXT, Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP);";
                command.ExecuteNonQuery();

                // Create Drivers table
                command.CommandText = "CREATE TABLE IF NOT EXISTS Drivers (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT);";
                command.ExecuteNonQuery();

                // Create Vehicles table
                command.CommandText = "CREATE TABLE IF NOT EXISTS Vehicles (Id INTEGER PRIMARY KEY AUTOINCREMENT, VehicleNumber TEXT);";
                command.ExecuteNonQuery();

                // Create Users table (simple local auth)
                command.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    PasswordSaltHex TEXT NOT NULL,
    PasswordHashHex TEXT NOT NULL,
    Role INTEGER NOT NULL
);";
                command.ExecuteNonQuery();

                command.CommandText = @"
CREATE TABLE IF NOT EXISTS CommercialCustomers (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL UNIQUE,
    Phone TEXT,
    Address TEXT,
    City TEXT,
    Area TEXT,
    Email TEXT,
    ConsumerId TEXT,
    ConsumerNo TEXT,
    DistributorCode TEXT,
    ConsumerType TEXT,
    ConsumerCategory TEXT,
    Product TEXT,
    LastRefillDate TEXT,
    Remarks TEXT,
    IsActive INTEGER NOT NULL DEFAULT 1,
    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);";
                command.ExecuteNonQuery();

                command.CommandText = @"
CREATE TABLE IF NOT EXISTS CommercialLedgerEntries (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    CustomerId INTEGER NOT NULL,
    EntryType INTEGER NOT NULL,
    AmountChange REAL NOT NULL DEFAULT 0,
    EmptyCylinderChange INTEGER NOT NULL DEFAULT 0,
    Filled19kgChange INTEGER NOT NULL DEFAULT 0,
    Notes TEXT,
    CreatedBy TEXT,
    Timestamp DATETIME DEFAULT CURRENT_TIMESTAMP,
    FOREIGN KEY (CustomerId) REFERENCES CommercialCustomers(Id)
);";
                command.ExecuteNonQuery();

                command.CommandText = @"
CREATE TABLE IF NOT EXISTS AccountingSettlements (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    DriverId INTEGER NOT NULL,
    VehicleId INTEGER NOT NULL,
    SettlementDate DATE NOT NULL,
    SavedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(DriverId, VehicleId, SettlementDate),
    FOREIGN KEY (DriverId) REFERENCES Drivers(Id),
    FOREIGN KEY (VehicleId) REFERENCES Vehicles(Id)
);";
                command.ExecuteNonQuery();

                command.CommandText = @"
CREATE TABLE IF NOT EXISTS AppConfig (
    Key TEXT PRIMARY KEY,
    Value TEXT,
    UpdatedAt DATETIME DEFAULT CURRENT_TIMESTAMP
);";
                command.ExecuteNonQuery();

                command.CommandText = "CREATE TABLE IF NOT EXISTS AppSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);";
                command.ExecuteNonQuery();

                command.CommandText = @"CREATE TABLE IF NOT EXISTS MorningUplifts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    WarehouseId INTEGER NOT NULL,
                    DriverId INTEGER NOT NULL,
                    VehicleId INTEGER NOT NULL,
                    BusinessDate TEXT NOT NULL,
                    ProductName TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    UNIQUE(WarehouseId, DriverId, VehicleId, BusinessDate, ProductName)
                );";
                command.ExecuteNonQuery();

                command.CommandText = @"CREATE TABLE IF NOT EXISTS DriverVehicleSettlements (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    WarehouseId INTEGER NOT NULL,
                    DriverId INTEGER NOT NULL,
                    VehicleId INTEGER NOT NULL,
                    BusinessDate TEXT NOT NULL,
                    CompletedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE(WarehouseId, DriverId, VehicleId, BusinessDate)
                );";
                command.ExecuteNonQuery();

                EnsureCommercialCustomerColumns(connection);
                EnsureAccountingSettlementColumns(connection);
                EnsureBusinessDateInitialized(connection);
                EnsureTransactionLogReversalColumns(connection);
            }

            EnsureDefaultCommercialCustomers();
        }

        private static void EnsureTransactionLogReversalColumns(SqliteConnection connection)
        {
            string[] alters =
            {
                "ALTER TABLE TransactionLogs ADD COLUMN IsReversed INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE TransactionLogs ADD COLUMN ReversalReason TEXT;",
                "ALTER TABLE TransactionLogs ADD COLUMN ReversedBy TEXT;",
                "ALTER TABLE TransactionLogs ADD COLUMN ReversedAt DATETIME;"
            };
            foreach (var sql in alters)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
                {
                    // Column already exists.
                }
            }
        }

        public List<TransactionLog> GetIoclPlantPurchases(int warehouseId, int limit = 50)
        {
            var list = new List<TransactionLog>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT t.Id, COALESCE(w.Name, 'Unknown'), t.CylinderType, t.FilledChange, t.EmptyChange,
       t.TransactionType, t.Timestamp, COALESCE(t.IsReversed, 0), COALESCE(t.ReversalReason, '')
FROM TransactionLogs t
LEFT JOIN Warehouses w ON w.Id = t.WarehouseId
WHERE t.WarehouseId = @wid
  AND t.TransactionType LIKE 'IOCL PURCHASE%'
  AND COALESCE(t.IsReversed, 0) = 0
ORDER BY t.Timestamp DESC, t.Id DESC
LIMIT @limit;";
            cmd.Parameters.AddWithValue("@wid", warehouseId);
            cmd.Parameters.AddWithValue("@limit", limit);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new TransactionLog
                {
                    Id = reader.GetInt32(0),
                    WarehouseName = reader.GetString(1),
                    CylinderType = reader.GetString(2),
                    FilledChange = reader.GetInt32(3),
                    EmptyChange = reader.GetInt32(4),
                    TransactionType = reader.GetString(5),
                    Timestamp = reader.GetString(6),
                    IsReversed = reader.GetInt32(7) == 1,
                    ReversalReason = reader.GetString(8)
                });
            }
            return list;
        }

        public bool TryReverseIoclPlantPurchase(int transactionId, string reason, string reversedBy, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrWhiteSpace(reason))
            {
                errorMessage = "Please select a reversal reason.";
                return false;
            }

            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            int warehouseId;
            string cylinderType;
            int filledChange;
            int emptyChange;
            string transactionType;

            using (var readCmd = connection.CreateCommand())
            {
                readCmd.Transaction = tx;
                readCmd.CommandText = @"
SELECT WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType, COALESCE(IsReversed, 0)
FROM TransactionLogs WHERE Id = @id;";
                readCmd.Parameters.AddWithValue("@id", transactionId);
                using var reader = readCmd.ExecuteReader();
                if (!reader.Read())
                {
                    errorMessage = "Transaction not found.";
                    return false;
                }

                warehouseId = reader.GetInt32(0);
                cylinderType = reader.GetString(1);
                filledChange = reader.GetInt32(2);
                emptyChange = reader.GetInt32(3);
                transactionType = reader.GetString(4);
                if (reader.GetInt32(5) == 1)
                {
                    errorMessage = "This entry has already been reversed.";
                    return false;
                }

                if (!transactionType.StartsWith("IOCL PURCHASE", StringComparison.OrdinalIgnoreCase))
                {
                    errorMessage = "Only IOCL plant purchase entries can be reversed here.";
                    return false;
                }
            }

            int currentFilled = 0;
            int currentEmpty = 0;
            using (var stockRead = connection.CreateCommand())
            {
                stockRead.Transaction = tx;
                stockRead.CommandText = "SELECT FilledQty, EmptyQty FROM StockMaster WHERE WarehouseId = @id AND CylinderType = @type;";
                stockRead.Parameters.AddWithValue("@id", warehouseId);
                stockRead.Parameters.AddWithValue("@type", cylinderType);
                using var stockReader = stockRead.ExecuteReader();
                if (!stockReader.Read())
                {
                    errorMessage = "Stock record not found for this product.";
                    return false;
                }
                currentFilled = stockReader.GetInt32(0);
                currentEmpty = stockReader.GetInt32(1);
            }

            int newFilled = currentFilled - filledChange;
            int newEmpty = currentEmpty - emptyChange;
            if (newFilled < 0 || newEmpty < 0)
            {
                errorMessage = $"Reversal would make stock negative (Filled would be {newFilled}, Empty would be {newEmpty}). Adjust stock manually if needed.";
                return false;
            }

            using (var stockUpdate = connection.CreateCommand())
            {
                stockUpdate.Transaction = tx;
                stockUpdate.CommandText = @"
UPDATE StockMaster SET FilledQty = FilledQty - @f, EmptyQty = EmptyQty - @e
WHERE WarehouseId = @id AND CylinderType = @type;";
                stockUpdate.Parameters.AddWithValue("@f", filledChange);
                stockUpdate.Parameters.AddWithValue("@e", emptyChange);
                stockUpdate.Parameters.AddWithValue("@id", warehouseId);
                stockUpdate.Parameters.AddWithValue("@type", cylinderType);
                stockUpdate.ExecuteNonQuery();
            }

            using (var markCmd = connection.CreateCommand())
            {
                markCmd.Transaction = tx;
                markCmd.CommandText = @"
UPDATE TransactionLogs
SET IsReversed = 1, ReversalReason = @reason, ReversedBy = @by, ReversedAt = CURRENT_TIMESTAMP
WHERE Id = @id;";
                markCmd.Parameters.AddWithValue("@reason", reason.Trim());
                markCmd.Parameters.AddWithValue("@by", reversedBy.Trim());
                markCmd.Parameters.AddWithValue("@id", transactionId);
                markCmd.ExecuteNonQuery();
            }

            using (var logCmd = connection.CreateCommand())
            {
                logCmd.Transaction = tx;
                logCmd.CommandText = @"
INSERT INTO TransactionLogs (WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType)
VALUES (@id, @type, @f, @e, @details);";
                logCmd.Parameters.AddWithValue("@id", warehouseId);
                logCmd.Parameters.AddWithValue("@type", cylinderType);
                logCmd.Parameters.AddWithValue("@f", -filledChange);
                logCmd.Parameters.AddWithValue("@e", -emptyChange);
                logCmd.Parameters.AddWithValue("@details",
                    $"IOCL PURCHASE REVERSAL | Reason: {reason.Trim()} | By: {reversedBy.Trim()} | Original Id: {transactionId} | {transactionType}");
                logCmd.ExecuteNonQuery();
            }

            tx.Commit();
            return true;
        }

        private static void EnsureAccountingSettlementColumns(SqliteConnection connection)
        {
            string[] alters =
            {
                "ALTER TABLE AccountingSettlements ADD COLUMN SummaryText TEXT;",
                "ALTER TABLE AccountingSettlements ADD COLUMN ExpectedRefills INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE AccountingSettlements ADD COLUMN RefillsEntered INTEGER NOT NULL DEFAULT 0;",
                "ALTER TABLE AccountingSettlements ADD COLUMN TotalCollection REAL NOT NULL DEFAULT 0;"
            };
            foreach (var sql in alters)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex) when (ex.Message.Contains("duplicate column"))
                {
                    // Column already exists.
                }
            }
        }

        private static void EnsureCommercialCustomerColumns(SqliteConnection connection)
        {
            string[] alters =
            {
                "ALTER TABLE CommercialCustomers ADD COLUMN Address TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN City TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN Area TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN Email TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN ConsumerId TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN ConsumerNo TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN DistributorCode TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN ConsumerType TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN ConsumerCategory TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN Product TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN LastRefillDate TEXT;",
                "ALTER TABLE CommercialCustomers ADD COLUMN Remarks TEXT;"
            };
            foreach (var sql in alters)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException)
                {
                    // Column already exists on upgraded databases.
                }
            }
        }

        private static readonly string[] DefaultCommercialCustomerNames =
        {
            "CUSTOMER F", "CUSTOMER G", "CUSTOMER H", "CUSTOMER C", "CUSTOMER D",
            "CUSTOMER E", "CUSTOMER I", "CUSTOMER J", "CUSTOMER K",
            "CUSTOMER A", "CUSTOMER B", "OTHER"
        };

        public void EnsureDefaultCommercialCustomers()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            foreach (var name in DefaultCommercialCustomerNames)
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
INSERT INTO CommercialCustomers (Name, IsActive)
SELECT @n, 1
WHERE NOT EXISTS (SELECT 1 FROM CommercialCustomers WHERE Name = @n);";
                cmd.Parameters.AddWithValue("@n", name);
                cmd.ExecuteNonQuery();
            }
        }

        public int AddCommercialCustomer(CommercialCustomerRegisterRow row)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO CommercialCustomers (
    Name, Phone, Address, City, Area, Email, ConsumerId, ConsumerNo,
    DistributorCode, ConsumerType, ConsumerCategory, Product, LastRefillDate, Remarks, IsActive)
VALUES (@n, @p, @a, @ci, @ar, @em, @cid, @cno, @dc, @ct, @cc, @pr, @lrd, @r, 1);";
            BindCustomerParams(cmd, row);
            cmd.ExecuteNonQuery();

            using var idCmd = connection.CreateCommand();
            idCmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt32(idCmd.ExecuteScalar() ?? 0);
        }

        public int AddCommercialCustomer(string name, string? phone = null)
        {
            return AddCommercialCustomer(new CommercialCustomerRegisterRow { Name = name, Phone = phone });
        }

        private static object DbOrNull(string? value) =>
            string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

        private static void BindCustomerParams(SqliteCommand cmd, CommercialCustomerRegisterRow row)
        {
            cmd.Parameters.AddWithValue("@n", row.Name.Trim());
            cmd.Parameters.AddWithValue("@p", DbOrNull(row.Phone));
            cmd.Parameters.AddWithValue("@a", DbOrNull(row.Address));
            cmd.Parameters.AddWithValue("@ci", DbOrNull(row.City));
            cmd.Parameters.AddWithValue("@ar", DbOrNull(row.Area));
            cmd.Parameters.AddWithValue("@em", DbOrNull(row.Email));
            cmd.Parameters.AddWithValue("@cid", DbOrNull(row.ConsumerId));
            cmd.Parameters.AddWithValue("@cno", DbOrNull(row.ConsumerNo));
            cmd.Parameters.AddWithValue("@dc", DbOrNull(row.DistributorCode));
            cmd.Parameters.AddWithValue("@ct", DbOrNull(row.ConsumerType));
            cmd.Parameters.AddWithValue("@cc", DbOrNull(row.ConsumerCategory));
            cmd.Parameters.AddWithValue("@pr", DbOrNull(row.Product));
            cmd.Parameters.AddWithValue("@lrd", DbOrNull(row.LastRefillDate));
            cmd.Parameters.AddWithValue("@r", DbOrNull(row.Remarks));
        }

        private static CommercialCustomer ReadCommercialCustomer(SqliteDataReader r) => new()
        {
            Id = r.GetInt32(0),
            Name = r.GetString(1),
            Phone = r.IsDBNull(2) ? null : r.GetString(2),
            Address = r.IsDBNull(3) ? null : r.GetString(3),
            City = r.IsDBNull(4) ? null : r.GetString(4),
            Area = r.IsDBNull(5) ? null : r.GetString(5),
            Email = r.IsDBNull(6) ? null : r.GetString(6),
            ConsumerId = r.IsDBNull(7) ? null : r.GetString(7),
            ConsumerNo = r.IsDBNull(8) ? null : r.GetString(8),
            DistributorCode = r.IsDBNull(9) ? null : r.GetString(9),
            ConsumerType = r.IsDBNull(10) ? null : r.GetString(10),
            ConsumerCategory = r.IsDBNull(11) ? null : r.GetString(11),
            Product = r.IsDBNull(12) ? null : r.GetString(12),
            LastRefillDate = r.IsDBNull(13) ? null : r.GetString(13),
            Remarks = r.IsDBNull(14) ? null : r.GetString(14),
            IsActive = r.GetInt32(15) == 1,
            CreatedAt = r.IsDBNull(16) ? DateTime.MinValue : r.GetDateTime(16)
        };

        public List<CommercialCustomer> GetCommercialCustomers(bool activeOnly = true)
        {
            var list = new List<CommercialCustomer>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = activeOnly
                ? @"SELECT Id, Name, Phone, Address, City, Area, Email, ConsumerId, ConsumerNo,
                    DistributorCode, ConsumerType, ConsumerCategory, Product, LastRefillDate, Remarks, IsActive, CreatedAt
                    FROM CommercialCustomers WHERE IsActive = 1 ORDER BY Name;"
                : @"SELECT Id, Name, Phone, Address, City, Area, Email, ConsumerId, ConsumerNo,
                    DistributorCode, ConsumerType, ConsumerCategory, Product, LastRefillDate, Remarks, IsActive, CreatedAt
                    FROM CommercialCustomers ORDER BY Name;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(ReadCommercialCustomer(r));
            return list;
        }

        public List<CommercialCustomerSummary> GetCommercialCustomerSummaries()
        {
            var list = new List<CommercialCustomerSummary>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
SELECT c.Id, c.Name, c.ConsumerId, c.Phone,
       COALESCE(SUM(e.AmountChange), 0),
       COALESCE(SUM(e.EmptyCylinderChange), 0),
       MAX(e.Timestamp)
FROM CommercialCustomers c
LEFT JOIN CommercialLedgerEntries e ON e.CustomerId = c.Id
WHERE c.IsActive = 1
GROUP BY c.Id, c.Name, c.ConsumerId, c.Phone
ORDER BY c.Name;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new CommercialCustomerSummary
                {
                    Id = r.GetInt32(0),
                    Name = r.GetString(1),
                    ConsumerId = r.IsDBNull(2) ? null : r.GetString(2),
                    Phone = r.IsDBNull(3) ? null : r.GetString(3),
                    AmountDue = Convert.ToDecimal(r.GetDouble(4)),
                    EmptyCylindersDue = r.GetInt32(5),
                    LastTransactionAt = r.IsDBNull(6) ? null : r.GetDateTime(6)
                });
            }
            return list;
        }

        public int? FindCommercialCustomerIdByName(string name, bool activeOnly = true)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = activeOnly
                ? "SELECT Id FROM CommercialCustomers WHERE LOWER(TRIM(Name)) = LOWER(TRIM(@n)) AND IsActive = 1 LIMIT 1;"
                : "SELECT Id FROM CommercialCustomers WHERE LOWER(TRIM(Name)) = LOWER(TRIM(@n)) LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            var result = cmd.ExecuteScalar();
            return result == null ? null : Convert.ToInt32(result);
        }

        public int? FindCommercialCustomerIdByConsumerId(string consumerId)
        {
            if (string.IsNullOrWhiteSpace(consumerId)) return null;
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id FROM CommercialCustomers WHERE TRIM(ConsumerId) = TRIM(@id) LIMIT 1;";
            cmd.Parameters.AddWithValue("@id", consumerId.Trim());
            var result = cmd.ExecuteScalar();
            return result == null ? null : Convert.ToInt32(result);
        }

        public int? FindCommercialCustomerIdByConsumerNo(string consumerNo)
        {
            if (string.IsNullOrWhiteSpace(consumerNo)) return null;
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT Id FROM CommercialCustomers WHERE TRIM(ConsumerNo) = TRIM(@no) LIMIT 1;";
            cmd.Parameters.AddWithValue("@no", consumerNo.Trim());
            var result = cmd.ExecuteScalar();
            return result == null ? null : Convert.ToInt32(result);
        }

        public int? FindCommercialCustomerId(CommercialCustomerRegisterRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.ConsumerId))
            {
                var byId = FindCommercialCustomerIdByConsumerId(row.ConsumerId);
                if (byId.HasValue) return byId;
            }
            if (!string.IsNullOrWhiteSpace(row.ConsumerNo))
            {
                var byNo = FindCommercialCustomerIdByConsumerNo(row.ConsumerNo);
                if (byNo.HasValue) return byNo;
            }
            return FindCommercialCustomerIdByName(row.Name, activeOnly: false);
        }

        public void UpdateCommercialCustomerDetails(int customerId, CommercialCustomerRegisterRow row)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
UPDATE CommercialCustomers SET
    Name = @n, Phone = @p, Address = @a, City = @ci, Area = @ar, Email = @em,
    ConsumerId = @cid, ConsumerNo = @cno, DistributorCode = @dc,
    ConsumerType = @ct, ConsumerCategory = @cc, Product = @pr,
    LastRefillDate = @lrd, Remarks = @r
WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            BindCustomerParams(cmd, row);
            cmd.ExecuteNonQuery();
        }

        public void ReactivateCommercialCustomer(int customerId)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE CommercialCustomers SET IsActive = 1 WHERE Id = @id;";
            cmd.Parameters.AddWithValue("@id", customerId);
            cmd.ExecuteNonQuery();
        }

        public CommercialCustomerImportResult ImportCommercialCustomers(
            IReadOnlyList<CommercialCustomerRegisterRow> rows,
            string? createdBy = null,
            bool postOpeningBalances = true)
        {
            var result = new CommercialCustomerImportResult { TotalRead = rows.Count };

            foreach (var row in rows)
            {
                var name = row.Name?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(name))
                {
                    result.Skipped++;
                    continue;
                }

                try
                {
                    int customerId;
                    var existingId = FindCommercialCustomerId(row);
                    if (existingId.HasValue)
                    {
                        customerId = existingId.Value;
                        ReactivateCommercialCustomer(customerId);
                        UpdateCommercialCustomerDetails(customerId, row);
                        result.Updated++;
                    }
                    else
                    {
                        customerId = AddCommercialCustomer(row);
                        result.Added++;
                    }

                    if (postOpeningBalances)
                        result.OpeningBalancesPosted += TryPostOpeningBalances(customerId, row, createdBy);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{name}: {ex.Message}");
                }
            }

            return result;
        }

        private int TryPostOpeningBalances(int customerId, CommercialCustomerRegisterRow row, string? createdBy)
        {
            int posted = 0;
            bool hasPayment = row.OpeningPaymentDue.HasValue && row.OpeningPaymentDue.Value != 0;
            bool hasEmpty = row.OpeningEmptyCylindersDue.HasValue && row.OpeningEmptyCylindersDue.Value != 0;

            if (!hasPayment && !hasEmpty)
                return 0;

            if (hasPayment)
            {
                AddCommercialLedgerEntry(
                    customerId,
                    CommercialLedgerEntryType.Adjustment,
                    row.OpeningPaymentDue!.Value,
                    0,
                    0,
                    "Opening balance from customer register import",
                    createdBy);
                posted++;
            }

            if (hasEmpty)
            {
                AddCommercialLedgerEntry(
                    customerId,
                    CommercialLedgerEntryType.Adjustment,
                    0,
                    row.OpeningEmptyCylindersDue!.Value,
                    row.OpeningEmptyCylindersDue.Value,
                    "Opening empty 19kg due from register import",
                    createdBy);
                posted++;
            }

            return posted;
        }

        public void AddCommercialLedgerEntry(
            int customerId,
            CommercialLedgerEntryType entryType,
            decimal amountChange,
            int emptyCylinderChange,
            int filled19kgChange,
            string? notes,
            string? createdBy)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
INSERT INTO CommercialLedgerEntries
    (CustomerId, EntryType, AmountChange, EmptyCylinderChange, Filled19kgChange, Notes, CreatedBy)
VALUES (@cid, @type, @amt, @empty, @filled, @notes, @by);";
            cmd.Parameters.AddWithValue("@cid", customerId);
            cmd.Parameters.AddWithValue("@type", (int)entryType);
            cmd.Parameters.AddWithValue("@amt", (double)amountChange);
            cmd.Parameters.AddWithValue("@empty", emptyCylinderChange);
            cmd.Parameters.AddWithValue("@filled", filled19kgChange);
            cmd.Parameters.AddWithValue("@notes", string.IsNullOrWhiteSpace(notes) ? DBNull.Value : notes.Trim());
            cmd.Parameters.AddWithValue("@by", string.IsNullOrWhiteSpace(createdBy) ? DBNull.Value : createdBy.Trim());
            cmd.ExecuteNonQuery();
        }

        public List<CommercialLedgerEntry> GetCommercialLedgerEntries(int? customerId = null, DateTime? from = null, DateTime? to = null)
        {
            var list = new List<CommercialLedgerEntry>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            var sql = @"
SELECT e.Id, e.CustomerId, c.Name, e.EntryType, e.AmountChange, e.EmptyCylinderChange,
       e.Filled19kgChange, e.Notes, e.CreatedBy, e.Timestamp
FROM CommercialLedgerEntries e
INNER JOIN CommercialCustomers c ON c.Id = e.CustomerId
WHERE 1=1";
            if (customerId.HasValue)
            {
                sql += " AND e.CustomerId = @cid";
                cmd.Parameters.AddWithValue("@cid", customerId.Value);
            }
            if (from.HasValue)
            {
                sql += " AND e.Timestamp >= @from";
                cmd.Parameters.AddWithValue("@from", from.Value.Date);
            }
            if (to.HasValue)
            {
                sql += " AND e.Timestamp < @to";
                cmd.Parameters.AddWithValue("@to", to.Value.Date.AddDays(1));
            }
            sql += " ORDER BY e.Timestamp DESC, e.Id DESC;";
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var entryType = (CommercialLedgerEntryType)r.GetInt32(3);
                list.Add(new CommercialLedgerEntry
                {
                    Id = r.GetInt32(0),
                    CustomerId = r.GetInt32(1),
                    CustomerName = r.GetString(2),
                    EntryType = entryType,
                    EntryTypeDisplay = FormatLedgerEntryType(entryType),
                    AmountChange = Convert.ToDecimal(r.GetDouble(4)),
                    EmptyCylinderChange = r.GetInt32(5),
                    Filled19kgChange = r.GetInt32(6),
                    Notes = r.IsDBNull(7) ? null : r.GetString(7),
                    CreatedBy = r.IsDBNull(8) ? null : r.GetString(8),
                    Timestamp = r.GetDateTime(9)
                });
            }
            return list;
        }

        private static string FormatLedgerEntryType(CommercialLedgerEntryType type) => type switch
        {
            CommercialLedgerEntryType.CreditSale19kg => "19kg Credit Sale",
            CommercialLedgerEntryType.PaymentReceived => "Payment Received",
            CommercialLedgerEntryType.EmptyReturned => "Empty Returned",
            CommercialLedgerEntryType.Adjustment => "Adjustment",
            _ => type.ToString()
        };

        /// <summary>
        /// Records hotel baki (commercial shortage) as empty-cylinder due for 19kg commercial.
        /// </summary>
        public bool TryRecordHotelBaki(int emptyCylindersMissing, string hotelName, string? notes, string? createdBy)
        {
            if (emptyCylindersMissing <= 0 || string.IsNullOrWhiteSpace(hotelName)) return false;

            var customerId = FindCommercialCustomerIdByName(hotelName);
            if (!customerId.HasValue)
            {
                customerId = AddCommercialCustomer(hotelName);
            }

            AddCommercialLedgerEntry(
                customerId.Value,
                CommercialLedgerEntryType.CreditSale19kg,
                amountChange: 0,
                emptyCylinderChange: emptyCylindersMissing,
                filled19kgChange: emptyCylindersMissing,
                notes: notes ?? $"Evening HOTEL BAKI — {emptyCylindersMissing} x 19kg empty due",
                createdBy: createdBy);

            return true;
        }

        public void EnsureDefaultUsers()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            // If no users exist, create a default Owner + Manager.
            int count = 0;
            try
            {
                using var countCmd = connection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(1) FROM Users;";
                count = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
            }
            catch (SqliteException)
            {
                // If DB is old/corrupted and Users table isn't readable, attempt to create it again.
                using var fix = connection.CreateCommand();
                fix.CommandText = @"
CREATE TABLE IF NOT EXISTS Users (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Username TEXT NOT NULL UNIQUE,
    PasswordSaltHex TEXT NOT NULL,
    PasswordHashHex TEXT NOT NULL,
    Role INTEGER NOT NULL
);";
                fix.ExecuteNonQuery();

                using var countCmd = connection.CreateCommand();
                countCmd.CommandText = "SELECT COUNT(1) FROM Users;";
                count = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
            }

            if (count > 0) return;

            CreateUserInternal(connection, username: "owner", password: DefaultPassword("GAM_OWNER_PASSWORD"), role: UserRole.Owner);
            CreateUserInternal(connection, username: "manager", password: DefaultPassword("GAM_MANAGER_PASSWORD"), role: UserRole.Manager);
        }

        // Initial passwords come from environment variables; "CHANGE_ME" is a placeholder only.
        // Change these passwords right after first login.
        private static string DefaultPassword(string envVar) =>
            Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } v ? v : "CHANGE_ME";

        private void CreateUserInternal(SqliteConnection connection, string username, string password, UserRole role)
        {
            var salt = RandomNumberGenerator.GetBytes(16);
            var (saltHex, hashHex) = HashPassword(password, salt);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO Users (Username, PasswordSaltHex, PasswordHashHex, Role) VALUES (@u, @s, @h, @r);";
            cmd.Parameters.AddWithValue("@u", username.Trim());
            cmd.Parameters.AddWithValue("@s", saltHex);
            cmd.Parameters.AddWithValue("@h", hashHex);
            cmd.Parameters.AddWithValue("@r", (int)role);
            cmd.ExecuteNonQuery();
        }

        public User? Authenticate(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username)) return null;
            using var connection = new SqliteConnection(connectionString);
            connection.Open();

            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Username, PasswordSaltHex, PasswordHashHex, Role FROM Users WHERE Username=@u;";
                cmd.Parameters.AddWithValue("@u", username.Trim());

                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                var id = r.GetInt32(0);
                var user = r.GetString(1);
                var saltHex = r.GetString(2);
                var expectedHashHex = r.GetString(3);
                var role = (UserRole)r.GetInt32(4);

                var salt = FromHex(saltHex);
                var (_, actualHashHex) = HashPassword(password ?? string.Empty, salt);

                if (!string.Equals(expectedHashHex, actualHashHex, StringComparison.OrdinalIgnoreCase))
                    return null;

                return new User { Id = id, Username = user, Role = role };
            }
            catch (SqliteException)
            {
                // Likely old DB without Users table; create defaults and retry once.
                InitializeDatabase();
                EnsureDefaultUsers();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT Id, Username, PasswordSaltHex, PasswordHashHex, Role FROM Users WHERE Username=@u;";
                cmd.Parameters.AddWithValue("@u", username.Trim());
                using var r = cmd.ExecuteReader();
                if (!r.Read()) return null;

                var id = r.GetInt32(0);
                var user = r.GetString(1);
                var saltHex = r.GetString(2);
                var expectedHashHex = r.GetString(3);
                var role = (UserRole)r.GetInt32(4);

                var salt = FromHex(saltHex);
                var (_, actualHashHex) = HashPassword(password ?? string.Empty, salt);

                if (!string.Equals(expectedHashHex, actualHashHex, StringComparison.OrdinalIgnoreCase))
                    return null;

                return new User { Id = id, Username = user, Role = role };
            }
        }

        public void SetMasterStockAbsolute(int warehouseId, string cylinderType, int filledQty, int emptyQty, string updatedByUsername)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var tx = connection.BeginTransaction();

            // Fetch current values
            int curFilled = 0, curEmpty = 0;
            using (var readCmd = connection.CreateCommand())
            {
                readCmd.CommandText = "SELECT FilledQty, EmptyQty FROM StockMaster WHERE WarehouseId=@id AND CylinderType=@t;";
                readCmd.Parameters.AddWithValue("@id", warehouseId);
                readCmd.Parameters.AddWithValue("@t", cylinderType);
                using var rr = readCmd.ExecuteReader();
                if (rr.Read())
                {
                    curFilled = rr.IsDBNull(0) ? 0 : rr.GetInt32(0);
                    curEmpty = rr.IsDBNull(1) ? 0 : rr.GetInt32(1);
                }
            }

            // Absolute set
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = @"
UPDATE StockMaster 
SET FilledQty = @f, EmptyQty = @e 
WHERE WarehouseId = @id AND CylinderType = @t;";
                cmd.Parameters.AddWithValue("@f", filledQty);
                cmd.Parameters.AddWithValue("@e", emptyQty);
                cmd.Parameters.AddWithValue("@id", warehouseId);
                cmd.Parameters.AddWithValue("@t", cylinderType);
                cmd.ExecuteNonQuery();
            }

            // Log as delta for reporting totals
            var df = filledQty - curFilled;
            var de = emptyQty - curEmpty;
            using (var logCmd = connection.CreateCommand())
            {
                logCmd.CommandText = @"INSERT INTO TransactionLogs (WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType) VALUES (@id, @type, @f, @e, @details)";
                logCmd.Parameters.AddWithValue("@id", warehouseId);
                logCmd.Parameters.AddWithValue("@type", cylinderType);
                logCmd.Parameters.AddWithValue("@f", df);
                logCmd.Parameters.AddWithValue("@e", de);
                logCmd.Parameters.AddWithValue("@details", $"MASTER STOCK UPDATE by {updatedByUsername}: set Filled={filledQty}, Empty={emptyQty}");
                logCmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private void EnsureBusinessDateInitialized(SqliteConnection connection)
        {
            var command = connection.CreateCommand();
            command.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key";
            command.Parameters.AddWithValue("@key", BusinessDateKey);
            var existing = command.ExecuteScalar();
            if (existing == null)
            {
                var insert = connection.CreateCommand();
                insert.CommandText = "INSERT INTO AppSettings (Key, Value) VALUES (@key, @value)";
                insert.Parameters.AddWithValue("@key", BusinessDateKey);
                insert.Parameters.AddWithValue("@value", DateTime.Today.ToString("yyyy-MM-dd"));
                insert.ExecuteNonQuery();
            }
        }

        public DateTime GetBusinessDate()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Value FROM AppSettings WHERE Key = @key";
                command.Parameters.AddWithValue("@key", BusinessDateKey);
                var value = command.ExecuteScalar()?.ToString();
                if (DateTime.TryParse(value, out var date))
                    return date.Date;
            }
            return DateTime.Today;
        }

        public void SetBusinessDate(DateTime date)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"INSERT INTO AppSettings (Key, Value) VALUES (@key, @value)
                    ON CONFLICT(Key) DO UPDATE SET Value = @value";
                command.Parameters.AddWithValue("@key", BusinessDateKey);
                command.Parameters.AddWithValue("@value", date.Date.ToString("yyyy-MM-dd"));
                command.ExecuteNonQuery();
            }
        }

        public DateTime AdvanceBusinessDate()
        {
            var nextDate = GetBusinessDate().AddDays(1);
            SetBusinessDate(nextDate);
            return nextDate;
        }

        public void SaveMorningUplifts(int warehouseId, int driverId, int vehicleId, string businessDate, Dictionary<string, int> uplifts)
        {
            EnsureMorningUpliftsTable();

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using var transaction = connection.BeginTransaction();

                var deleteCmd = connection.CreateCommand();
                deleteCmd.Transaction = transaction;
                deleteCmd.CommandText = @"DELETE FROM MorningUplifts
                        WHERE WarehouseId = @wid AND DriverId = @did AND VehicleId = @vid AND BusinessDate = @date";
                deleteCmd.Parameters.AddWithValue("@wid", warehouseId);
                deleteCmd.Parameters.AddWithValue("@did", driverId);
                deleteCmd.Parameters.AddWithValue("@vid", vehicleId);
                deleteCmd.Parameters.AddWithValue("@date", businessDate);
                deleteCmd.ExecuteNonQuery();

                foreach (var uplift in uplifts)
                {
                    var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText = @"INSERT INTO MorningUplifts
                            (WarehouseId, DriverId, VehicleId, BusinessDate, ProductName, Quantity)
                            VALUES (@wid, @did, @vid, @date, @product, @qty)";
                    insertCmd.Parameters.AddWithValue("@wid", warehouseId);
                    insertCmd.Parameters.AddWithValue("@did", driverId);
                    insertCmd.Parameters.AddWithValue("@vid", vehicleId);
                    insertCmd.Parameters.AddWithValue("@date", businessDate);
                    insertCmd.Parameters.AddWithValue("@product", uplift.Key);
                    insertCmd.Parameters.AddWithValue("@qty", uplift.Value);
                    insertCmd.ExecuteNonQuery();
                }

                transaction.Commit();
            }
        }

        public void EnsureMorningUpliftsTable()
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"CREATE TABLE IF NOT EXISTS MorningUplifts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    WarehouseId INTEGER NOT NULL,
                    DriverId INTEGER NOT NULL,
                    VehicleId INTEGER NOT NULL,
                    BusinessDate TEXT NOT NULL,
                    ProductName TEXT NOT NULL,
                    Quantity INTEGER NOT NULL,
                    UNIQUE(WarehouseId, DriverId, VehicleId, BusinessDate, ProductName)
                );";
            command.ExecuteNonQuery();
        }

        public Dictionary<string, int> GetMorningUplifts(int warehouseId, int driverId, int vehicleId, string businessDate)
        {
            var uplifts = new Dictionary<string, int>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"SELECT ProductName, Quantity FROM MorningUplifts
                    WHERE WarehouseId = @wid AND DriverId = @did AND VehicleId = @vid AND BusinessDate = @date";
                command.Parameters.AddWithValue("@wid", warehouseId);
                command.Parameters.AddWithValue("@did", driverId);
                command.Parameters.AddWithValue("@vid", vehicleId);
                command.Parameters.AddWithValue("@date", businessDate);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                        uplifts[reader.GetString(0)] = reader.GetInt32(1);
                }
            }
            return uplifts;
        }

        public List<(int DriverId, int VehicleId)> GetMorningOutPairsForDate(int warehouseId, string businessDate)
        {
            var pairs = new List<(int, int)>();
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT DISTINCT DriverId, VehicleId FROM MorningUplifts
                WHERE WarehouseId = @wid AND BusinessDate = @date
                ORDER BY DriverId, VehicleId";
            command.Parameters.AddWithValue("@wid", warehouseId);
            command.Parameters.AddWithValue("@date", businessDate);
            using var reader = command.ExecuteReader();
            while (reader.Read())
                pairs.Add((reader.GetInt32(0), reader.GetInt32(1)));
            return pairs;
        }

        public bool HasMorningOut(int warehouseId, int driverId, int vehicleId, string businessDate)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"SELECT COUNT(1) FROM MorningUplifts
                WHERE WarehouseId = @wid AND DriverId = @did AND VehicleId = @vid AND BusinessDate = @date";
            command.Parameters.AddWithValue("@wid", warehouseId);
            command.Parameters.AddWithValue("@did", driverId);
            command.Parameters.AddWithValue("@vid", vehicleId);
            command.Parameters.AddWithValue("@date", businessDate);
            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        public bool IsSettlementCompleted(int warehouseId, int driverId, int vehicleId, string businessDate)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"SELECT COUNT(1) FROM DriverVehicleSettlements
                    WHERE WarehouseId = @wid AND DriverId = @did AND VehicleId = @vid AND BusinessDate = @date";
                command.Parameters.AddWithValue("@wid", warehouseId);
                command.Parameters.AddWithValue("@did", driverId);
                command.Parameters.AddWithValue("@vid", vehicleId);
                command.Parameters.AddWithValue("@date", businessDate);
                return Convert.ToInt32(command.ExecuteScalar()) > 0;
            }
        }

        public void MarkSettlementCompleted(int warehouseId, int driverId, int vehicleId, string businessDate)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"INSERT OR IGNORE INTO DriverVehicleSettlements
                    (WarehouseId, DriverId, VehicleId, BusinessDate)
                    VALUES (@wid, @did, @vid, @date)";
                command.Parameters.AddWithValue("@wid", warehouseId);
                command.Parameters.AddWithValue("@did", driverId);
                command.Parameters.AddWithValue("@vid", vehicleId);
                command.Parameters.AddWithValue("@date", businessDate);
                command.ExecuteNonQuery();
            }
        }

        public List<DriverVehicleSettlement> GetCompletedSettlementsForDate(int warehouseId, string businessDate)
        {
            var list = new List<DriverVehicleSettlement>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"SELECT s.Id, s.WarehouseId, s.DriverId, s.VehicleId, s.BusinessDate, s.CompletedAt,
                        d.Name, v.VehicleNumber
                    FROM DriverVehicleSettlements s
                    JOIN Drivers d ON d.Id = s.DriverId
                    JOIN Vehicles v ON v.Id = s.VehicleId
                    WHERE s.WarehouseId = @wid AND s.BusinessDate = @date
                    ORDER BY s.CompletedAt DESC";
                command.Parameters.AddWithValue("@wid", warehouseId);
                command.Parameters.AddWithValue("@date", businessDate);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new DriverVehicleSettlement
                        {
                            Id = reader.GetInt32(0),
                            WarehouseId = reader.GetInt32(1),
                            DriverId = reader.GetInt32(2),
                            VehicleId = reader.GetInt32(3),
                            BusinessDate = reader.GetString(4),
                            CompletedAt = DateTime.Parse(reader.GetString(5)),
                            DriverName = reader.GetString(6),
                            VehicleNumber = reader.GetString(7)
                        });
                    }
                }
            }
            return list;
        }

        public void EnsureStockRowsExist()
        {
            using (var connection = new SqliteConnection(ConnectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();

                string[] allProducts = {
                    "14.2kg Domestic",
                    "19kg Commercial",
                    "5kg Domestic",
                    "10kg Domestic",
                    "5kg FTL",
                    "2kg FTL"
                };

                foreach (var product in allProducts)
                {
                    command.CommandText = @"
                INSERT INTO StockMaster (WarehouseId, CylinderType, FilledQty, EmptyQty)
                SELECT Id, @product, 0, 0
                FROM Warehouses
                WHERE NOT EXISTS (
                    SELECT 1 FROM StockMaster 
                    WHERE WarehouseId = Warehouses.Id 
                    AND CylinderType = @product
                );";

                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@product", product);
                    command.ExecuteNonQuery();
                }
            }
        }

        public List<Warehouse> GetWarehouses()
        {
            var list = new List<Warehouse>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT Id, Name FROM Warehouses";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new Warehouse { Id = reader.GetInt32(0), Name = reader.GetString(1) });
                    }
                }
            }
            return list;
        }

        public void AddWarehouse(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Warehouse name cannot be empty", nameof(name));

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "INSERT INTO Warehouses (Name) VALUES (@name)";
                command.Parameters.AddWithValue("@name", name.Trim());
                command.ExecuteNonQuery();
            }
        }

        public void RemoveWarehouse(int warehouseId)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM Warehouses WHERE Id = @id";
                command.Parameters.AddWithValue("@id", warehouseId);
                command.ExecuteNonQuery();
            }
        }

        public List<StockItem> GetStockForWarehouse(int warehouseId, string type)
        {
            var list = new List<StockItem>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT WarehouseId, CylinderType, FilledQty, EmptyQty FROM StockMaster WHERE WarehouseId = @id AND CylinderType = @type";
                command.Parameters.AddWithValue("@id", warehouseId);
                command.Parameters.AddWithValue("@type", type);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new StockItem { WarehouseId = reader.GetInt32(0), CylinderType = reader.GetString(1), FilledQty = reader.GetInt32(2), EmptyQty = reader.GetInt32(3) });
                    }
                }
            }
            return list;
        }

        public List<StockItem> GetAllStockForWarehouse(int warehouseId)
        {
            var list = new List<StockItem>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT WarehouseId, CylinderType, FilledQty, EmptyQty FROM StockMaster WHERE WarehouseId = @id ORDER BY CylinderType";
                command.Parameters.AddWithValue("@id", warehouseId);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new StockItem { WarehouseId = reader.GetInt32(0), CylinderType = reader.GetString(1), FilledQty = reader.GetInt32(2), EmptyQty = reader.GetInt32(3) });
                    }
                }
            }
            return list;
        }

        public void UpdateStock(int warehouseId, string cylinderType, int filledChange, int emptyChange, string type)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    var command = connection.CreateCommand();
                    command.CommandText = @"UPDATE StockMaster SET FilledQty = FilledQty + @f, EmptyQty = EmptyQty + @e WHERE WarehouseId = @id AND CylinderType = @type";
                    command.Parameters.AddWithValue("@f", filledChange);
                    command.Parameters.AddWithValue("@e", emptyChange);
                    command.Parameters.AddWithValue("@id", warehouseId);
                    command.Parameters.AddWithValue("@type", cylinderType);
                    command.ExecuteNonQuery();

                    var logCmd = connection.CreateCommand();
                    logCmd.CommandText = @"INSERT INTO TransactionLogs (WarehouseId, CylinderType, FilledChange, EmptyChange, TransactionType) VALUES (@id, @type, @f, @e, @t)";
                    logCmd.Parameters.AddWithValue("@id", warehouseId);
                    logCmd.Parameters.AddWithValue("@type", cylinderType);
                    logCmd.Parameters.AddWithValue("@f", filledChange);
                    logCmd.Parameters.AddWithValue("@e", emptyChange);
                    logCmd.Parameters.AddWithValue("@t", type);
                    logCmd.ExecuteNonQuery();

                    transaction.Commit();
                }
            }
        }

        // Admin operations for drivers and vehicles
        public int AddDriver(string name)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO Drivers (Name) VALUES (@name);";
                cmd.Parameters.AddWithValue("@name", name);
                cmd.ExecuteNonQuery();

                var idCmd = connection.CreateCommand();
                idCmd.CommandText = "SELECT last_insert_rowid();";
                var id = (long)idCmd.ExecuteScalar();
                return (int)id;
            }
        }

        public void RemoveDriver(int id)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Drivers WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateDriver(int id, string name)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Drivers SET Name = @name WHERE Id = @id";
            cmd.Parameters.AddWithValue("@name", name.Trim());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        public int AddVehicle(string vehicleNumber)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "INSERT INTO Vehicles (VehicleNumber) VALUES (@v);";
                cmd.Parameters.AddWithValue("@v", vehicleNumber);
                cmd.ExecuteNonQuery();

                var idCmd = connection.CreateCommand();
                idCmd.CommandText = "SELECT last_insert_rowid();";
                var id = (long)idCmd.ExecuteScalar();
                return (int)id;
            }
        }

        public void RemoveVehicle(int id)
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "DELETE FROM Vehicles WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdateVehicle(int id, string vehicleNumber)
        {
            using var connection = new SqliteConnection(connectionString);
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "UPDATE Vehicles SET VehicleNumber = @v WHERE Id = @id";
            cmd.Parameters.AddWithValue("@v", vehicleNumber.Trim());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ============= Accounting Settlement & Date Management Methods =============

        /// <summary>
        /// Gets the current working date from AppConfig table.
        /// Returns today's date if not set in database.
        /// </summary>
        public DateTime GetCurrentWorkingDate()
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = "SELECT Value FROM AppConfig WHERE Key = 'CurrentWorkingDate'";
                    var result = cmd.ExecuteScalar();

                    if (result != null && DateTime.TryParse(result.ToString(), out var date))
                    {
                        return date.Date;
                    }
                }
            }
            catch
            {
                // If table doesn't exist or query fails, return today
            }

            return DateTime.Today;
        }

        /// <summary>
        /// Sets the current working date in AppConfig table.
        /// </summary>
        public void SetCurrentWorkingDate(DateTime date)
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO AppConfig (Key, Value, UpdatedAt) 
VALUES ('CurrentWorkingDate', @value, CURRENT_TIMESTAMP)
ON CONFLICT(Key) DO UPDATE SET Value = @value, UpdatedAt = CURRENT_TIMESTAMP";
                    cmd.Parameters.AddWithValue("@value", date.Date.ToString("yyyy-MM-dd"));
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error setting working date: {ex.Message}");
            }
        }

        public bool HasAnyEveningSettlementForDate(int warehouseId, string businessDate)
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"SELECT COUNT(*) FROM DriverVehicleSettlements
                        WHERE WarehouseId = @wid AND BusinessDate = @date";
                    cmd.Parameters.AddWithValue("@wid", warehouseId);
                    cmd.Parameters.AddWithValue("@date", businessDate);
                    var count = cmd.ExecuteScalar();
                    return count != null && Convert.ToInt64(count) > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public List<DriverVehicleSettlement> GetAccountingPendingTrips(int warehouseId, string businessDate)
        {
            var list = new List<DriverVehicleSettlement>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var command = connection.CreateCommand();
                command.CommandText = @"SELECT s.Id, s.WarehouseId, s.DriverId, s.VehicleId, s.BusinessDate, s.CompletedAt,
                        d.Name, v.VehicleNumber
                    FROM DriverVehicleSettlements s
                    JOIN Drivers d ON d.Id = s.DriverId
                    JOIN Vehicles v ON v.Id = s.VehicleId
                    WHERE s.WarehouseId = @wid AND s.BusinessDate = @date
                    AND NOT EXISTS (
                        SELECT 1 FROM AccountingSettlements a
                        WHERE a.DriverId = s.DriverId AND a.VehicleId = s.VehicleId
                        AND a.SettlementDate = s.BusinessDate
                    )
                    ORDER BY d.Name, v.VehicleNumber";
                command.Parameters.AddWithValue("@wid", warehouseId);
                command.Parameters.AddWithValue("@date", businessDate);
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new DriverVehicleSettlement
                        {
                            Id = reader.GetInt32(0),
                            WarehouseId = reader.GetInt32(1),
                            DriverId = reader.GetInt32(2),
                            VehicleId = reader.GetInt32(3),
                            BusinessDate = reader.GetString(4),
                            CompletedAt = DateTime.Parse(reader.GetString(5)),
                            DriverName = reader.GetString(6),
                            VehicleNumber = reader.GetString(7)
                        });
                    }
                }
            }
            return list;
        }

        public TripAccountingSummary GetTripAccountingSummary(int warehouseId, int driverId, int vehicleId, string businessDate)
        {
            var summary = new TripAccountingSummary();
            var uplifts = GetMorningUplifts(warehouseId, driverId, vehicleId, businessDate);
            summary.MorningTotal = uplifts.Values.Sum();

            var driverName = GetDrivers().FirstOrDefault(d => d.Id == driverId)?.Name ?? "";
            var vehicleNumber = GetVehicles().FirstOrDefault(v => v.Id == vehicleId)?.VehicleNumber ?? "";

            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"SELECT COALESCE(SUM(EmptyChange), 0), COALESCE(SUM(FilledChange), 0)
                    FROM TransactionLogs
                    WHERE WarehouseId = @wid
                    AND TransactionType LIKE '%Accounting Settlement%'
                    AND TransactionType LIKE @driverPat
                    AND TransactionType LIKE @vehiclePat
                    AND TransactionType LIKE @datePat";
                cmd.Parameters.AddWithValue("@wid", warehouseId);
                cmd.Parameters.AddWithValue("@driverPat", $"%Driver: {driverName}%");
                cmd.Parameters.AddWithValue("@vehiclePat", $"%Vehicle: {vehicleNumber}%");
                cmd.Parameters.AddWithValue("@datePat", $"%Date: {businessDate}%");
                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        summary.EmptiesReturned = reader.GetInt32(0);
                        summary.UnsoldFilledReturned = reader.GetInt32(1);
                    }
                }
            }

            return summary;
        }

        public List<AccountingSettlementRecord> GetAccountingSettlementHistory(string? fromDate, string? toDate)
        {
            var list = new List<AccountingSettlementRecord>();
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = @"
SELECT a.Id, a.DriverId, a.VehicleId, a.SettlementDate, a.SavedAt,
       COALESCE(a.ExpectedRefills, 0), COALESCE(a.RefillsEntered, 0), COALESCE(a.TotalCollection, 0),
       d.Name, v.VehicleNumber, COALESCE(a.SummaryText, '')
FROM AccountingSettlements a
JOIN Drivers d ON d.Id = a.DriverId
JOIN Vehicles v ON v.Id = a.VehicleId
WHERE (@fromDate IS NULL OR a.SettlementDate >= @fromDate)
  AND (@toDate IS NULL OR a.SettlementDate <= @toDate)
ORDER BY a.SettlementDate DESC, a.SavedAt DESC";
                cmd.Parameters.AddWithValue("@fromDate", string.IsNullOrWhiteSpace(fromDate) ? DBNull.Value : fromDate);
                cmd.Parameters.AddWithValue("@toDate", string.IsNullOrWhiteSpace(toDate) ? DBNull.Value : toDate);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new AccountingSettlementRecord
                    {
                        Id = reader.GetInt32(0),
                        DriverId = reader.GetInt32(1),
                        VehicleId = reader.GetInt32(2),
                        SettlementDate = reader.GetString(3),
                        SavedAt = DateTime.Parse(reader.GetString(4)),
                        ExpectedRefills = reader.GetInt32(5),
                        RefillsEntered = reader.GetInt32(6),
                        TotalCollection = reader.GetDecimal(7),
                        DriverName = reader.GetString(8),
                        VehicleNumber = reader.GetString(9),
                        SummaryText = reader.GetString(10)
                    });
                }
            }
            return list;
        }

        public bool HasAnyAccountingSettlement()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM AccountingSettlements";
                var count = cmd.ExecuteScalar();
                return count != null && Convert.ToInt64(count) > 0;
            }
        }

        /// <summary>
        /// Records an accounting settlement for a driver/vehicle/date combination.
        /// </summary>
        public bool RecordAccountingSettlement(
            int driverId,
            int vehicleId,
            DateTime settlementDate,
            string summaryText,
            int expectedRefills,
            int refillsEntered,
            decimal totalCollection)
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO AccountingSettlements
    (DriverId, VehicleId, SettlementDate, SavedAt, SummaryText, ExpectedRefills, RefillsEntered, TotalCollection)
VALUES (@driverId, @vehicleId, @date, CURRENT_TIMESTAMP, @summary, @expected, @entered, @total)";
                    cmd.Parameters.AddWithValue("@driverId", driverId);
                    cmd.Parameters.AddWithValue("@vehicleId", vehicleId);
                    cmd.Parameters.AddWithValue("@date", settlementDate.Date.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("@summary", summaryText ?? string.Empty);
                    cmd.Parameters.AddWithValue("@expected", expectedRefills);
                    cmd.Parameters.AddWithValue("@entered", refillsEntered);
                    cmd.Parameters.AddWithValue("@total", totalCollection);
                    cmd.ExecuteNonQuery();
                    return true;
                }
            }
            catch (SqliteException ex) when (ex.Message.Contains("UNIQUE constraint failed"))
            {
                // Settlement already exists for this combination
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error recording settlement: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks if accounting settlement already exists for a driver/vehicle/date combination.
        /// </summary>
        public bool IsAccountingSettlementExists(int driverId, int vehicleId, DateTime settlementDate)
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    connection.Open();
                    var cmd = connection.CreateCommand();
                    cmd.CommandText = @"
SELECT COUNT(*) FROM AccountingSettlements 
WHERE DriverId = @driverId AND VehicleId = @vehicleId AND SettlementDate = @date";
                    cmd.Parameters.AddWithValue("@driverId", driverId);
                    cmd.Parameters.AddWithValue("@vehicleId", vehicleId);
                    cmd.Parameters.AddWithValue("@date", settlementDate.Date.ToString("yyyy-MM-dd"));
                    var count = cmd.ExecuteScalar();
                    return count != null && (long)count > 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}