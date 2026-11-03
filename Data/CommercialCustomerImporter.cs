using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GasAgencyManager.Models;

namespace GasAgencyManager.Data
{
    public static class CommercialCustomerImporter
    {
        /// <summary>IOCL register export — columns used by the ledger import.</summary>
        public static readonly string[] RegisterColumnGuide =
        {
            "Consumer Name (required)",
            "Consumer ID",
            "Consumer Number",
            "Phone Number",
            "Address, City, Area Name",
            "Consumer Type / Category / Product (for 19kg commercial filter)",
            "Last Refill Date",
            "+ optional: Payment Due, Empty 19kg Due if you add those columns"
        };

        /// <summary>Full IOCL header row (export as-is from your register).</summary>
        public const string IoclHeaderRow =
            "Distributor Code,Distributor Name,Consumer ID,Consumer Number,Consumer Name,Address,City,Area Name,Latitude,Longitude,Email Address (Register),Phone Number,Consumer Status,Consumer Sub Status,Consumer Type,Consumer Category,Subsidy Trans,Subsidy Status,KYC Number,KYC Date,SV/TSV (Order Number),Order date,Scheme Type,Scheme Selected,Scheme Subtype,Creation Channel,Product,Order Type,Mandatory Inspection Date,Tube Change Date,Tube Change Due Date,Delivery Type,Scheme Opted,District Name,e-KYC Type,e-KYC Date,e-KYC Flag,BSC_DUE_FLG,X_BSC_DT,Adhaar Available,Adhaar Number,Ration Available,Ration Number,Last Refill Date";

        public static readonly string SampleCsvContent =
            IoclHeaderRow + "\n" +
            "DIST001,Sample Distributor,100001,SV-100001,SAMPLE CUSTOMER,Main Road,Sample City,Route A,,,customer@example.com,0000000000,Active,,Commercial,19KG,,,,,,SV-ORD-001,,,,,,,19 KG Commercial,,,,,,,,,,,,,,,,,01-Jan-2026\n";

        public static List<CommercialCustomerRegisterRow> ParseCsvFile(string filePath)
        {
            var text = File.ReadAllText(filePath, Encoding.UTF8);
            return ParseCsv(text);
        }

        public static List<CommercialCustomerRegisterRow> ParseCsv(string csvText, bool commercialOnly = false)
        {
            var rows = new List<CommercialCustomerRegisterRow>();
            if (string.IsNullOrWhiteSpace(csvText))
                return rows;

            var lines = csvText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            ColumnMap? map = null;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line))
                    continue;

                var fields = SplitCsvLine(line);
                if (fields.Count == 0)
                    continue;

                if (map == null)
                {
                    if (TryBuildColumnMap(fields, out var headerMap))
                    {
                        map = headerMap;
                        continue;
                    }
                    map = ColumnMap.ForLegacyTwoColumn();
                }

                var row = map.ParseRow(fields);
                if (string.IsNullOrWhiteSpace(row.Name))
                    continue;

                if (commercialOnly && !row.LooksCommercial())
                    continue;

                rows.Add(row);
            }

            return rows;
        }

        private static bool TryBuildColumnMap(List<string> fields, out ColumnMap map)
        {
            map = new ColumnMap();
            bool recognized = false;

            for (int i = 0; i < fields.Count; i++)
            {
                var key = NormalizeHeader(fields[i]);
                if (string.IsNullOrEmpty(key))
                    continue;

                var kind = ClassifyHeader(key);
                if (kind == HeaderKind.Unknown)
                    continue;

                map.Set(kind, i);
                recognized = true;
            }

            if (!recognized)
                return false;

            if (!map.Has(HeaderKind.ConsumerName))
                map.Set(HeaderKind.ConsumerName, map.Has(HeaderKind.ConsumerNumber) ? 4 : 0);

            return true;
        }

        private static string NormalizeHeader(string header)
        {
            var s = header.Trim().ToLowerInvariant();
            s = s.Replace("(", " ").Replace(")", " ").Replace("/", " ");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            return s;
        }

        private enum HeaderKind
        {
            Unknown,
            DistributorCode,
            DistributorName,
            ConsumerId,
            ConsumerNumber,
            ConsumerName,
            Address,
            City,
            AreaName,
            Email,
            Phone,
            ConsumerStatus,
            ConsumerSubStatus,
            ConsumerType,
            ConsumerCategory,
            Product,
            SvOrderNo,
            DistrictName,
            LastRefillDate,
            PaymentDue,
            EmptyDue,
            Remarks
        }

        private static HeaderKind ClassifyHeader(string header)
        {
            if (header is "distributor code") return HeaderKind.DistributorCode;
            if (header is "distributor name") return HeaderKind.DistributorName;
            if (header is "consumer id") return HeaderKind.ConsumerId;
            if (header is "consumer number") return HeaderKind.ConsumerNumber;
            if (header is "consumer name") return HeaderKind.ConsumerName;
            if (header is "address" or "addr") return HeaderKind.Address;
            if (header is "city") return HeaderKind.City;
            if (header is "area name" or "area") return HeaderKind.AreaName;
            if (header.Contains("email")) return HeaderKind.Email;
            if (header is "phone number" || header.Contains("phone") || header.Contains("mobile"))
                return HeaderKind.Phone;
            if (header is "consumer status") return HeaderKind.ConsumerStatus;
            if (header is "consumer sub status") return HeaderKind.ConsumerSubStatus;
            if (header is "consumer type") return HeaderKind.ConsumerType;
            if (header is "consumer category") return HeaderKind.ConsumerCategory;
            if (header is "product") return HeaderKind.Product;
            if (header.Contains("sv tsv") || (header.Contains("order number") && header.Contains("sv")))
                return HeaderKind.SvOrderNo;
            if (header is "district name" or "district") return HeaderKind.DistrictName;
            if (header is "last refill date") return HeaderKind.LastRefillDate;

            if (header is "name" or "customer name" or "hotel" or "party name" or "shop")
                return HeaderKind.ConsumerName;
            if (header is "consumer no" || header.Contains("sv no")) return HeaderKind.ConsumerNumber;
            if (header is "remarks" or "note") return HeaderKind.Remarks;

            if (header.Contains("payment due") || header.Contains("amount due") || header is "baki" || header.Contains("balance"))
                return HeaderKind.PaymentDue;
            if (header.Contains("empty") && (header.Contains("due") || header.Contains("19")))
                return HeaderKind.EmptyDue;

            return HeaderKind.Unknown;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else inQuotes = !inQuotes;
                }
                else if ((c == ',' || c == '\t' || c == ';') && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }

            result.Add(current.ToString());
            return result;
        }

        private static string? FieldAt(List<string> fields, int index)
        {
            if (index < 0 || index >= fields.Count) return null;
            var v = fields[index].Trim();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }

        private static decimal? ParseDecimal(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Replace("₹", "").Replace(",", "").Trim();
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)) return d;
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out d)) return d;
            return null;
        }

        private static int? ParseInt(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Replace(",", "").Trim();
            if (int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var n)) return n;
            if (int.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out n)) return n;
            return null;
        }

        private sealed class ColumnMap
        {
            private readonly Dictionary<HeaderKind, int> _indices = new();

            public void Set(HeaderKind kind, int index) => _indices[kind] = index;
            public bool Has(HeaderKind kind) => _indices.ContainsKey(kind);
            public int Idx(HeaderKind kind) => _indices.TryGetValue(kind, out var i) ? i : -1;

            public static ColumnMap ForLegacyTwoColumn()
            {
                var m = new ColumnMap();
                m.Set(HeaderKind.ConsumerName, 0);
                m.Set(HeaderKind.Phone, 1);
                return m;
            }

            public CommercialCustomerRegisterRow ParseRow(List<string> fields)
            {
                var row = new CommercialCustomerRegisterRow
                {
                    Name = FieldAt(fields, Idx(HeaderKind.ConsumerName)) ?? "",
                    Phone = FieldAt(fields, Idx(HeaderKind.Phone)),
                    Address = FieldAt(fields, Idx(HeaderKind.Address)),
                    City = FieldAt(fields, Idx(HeaderKind.City)),
                    Area = FieldAt(fields, Idx(HeaderKind.AreaName)),
                    Email = FieldAt(fields, Idx(HeaderKind.Email)),
                    ConsumerId = FieldAt(fields, Idx(HeaderKind.ConsumerId)),
                    ConsumerNo = FieldAt(fields, Idx(HeaderKind.ConsumerNumber)),
                    DistributorCode = FieldAt(fields, Idx(HeaderKind.DistributorCode)),
                    DistributorName = FieldAt(fields, Idx(HeaderKind.DistributorName)),
                    ConsumerStatus = FieldAt(fields, Idx(HeaderKind.ConsumerStatus)),
                    ConsumerSubStatus = FieldAt(fields, Idx(HeaderKind.ConsumerSubStatus)),
                    ConsumerType = FieldAt(fields, Idx(HeaderKind.ConsumerType)),
                    ConsumerCategory = FieldAt(fields, Idx(HeaderKind.ConsumerCategory)),
                    Product = FieldAt(fields, Idx(HeaderKind.Product)),
                    SvOrderNo = FieldAt(fields, Idx(HeaderKind.SvOrderNo)),
                    District = FieldAt(fields, Idx(HeaderKind.DistrictName)),
                    LastRefillDate = FieldAt(fields, Idx(HeaderKind.LastRefillDate)),
                    Remarks = FieldAt(fields, Idx(HeaderKind.Remarks)),
                    OpeningPaymentDue = ParseDecimal(FieldAt(fields, Idx(HeaderKind.PaymentDue))),
                    OpeningEmptyCylindersDue = ParseInt(FieldAt(fields, Idx(HeaderKind.EmptyDue)))
                };

                if (string.IsNullOrWhiteSpace(row.ConsumerNo) && !string.IsNullOrWhiteSpace(row.SvOrderNo))
                    row.ConsumerNo = row.SvOrderNo;

                return row;
            }
        }
    }
}


