using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Data.SqlClient;
using MriAPPDriverShared.Models;

namespace MriAPPDriverShared.Data
{
    public class DriverRepository
    {
        private readonly string _connectionString;

        public DriverRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <summary>
        /// Looks up report context for a given MriAPPDriverProcessId.
        /// Parses MriAPPDriverProcessId from the ServerInfo column and
        /// joins to get SessionId, UserId, StartTime, and ReportName.
        /// </summary>
        public async Task<DriverProcessInfo?> GetProcessInfoAsync(int processId)
        {
            const string sql = @"
                SELECT TOP 1
                    CAST(Session_Id AS VARCHAR(50))       AS SessionId,
                    UserId,
                    ProcessStartTime                      AS StartTime,
                    Description                           AS ReportName,
                    ComputerName
                FROM MRI_Server_Messages
                WHERE ServerInfo LIKE '%MriAPPDriverProcessId=' + CAST(@ProcessId AS VARCHAR(20)) + '%'
                  AND Task IN ('REPORT', 'BATCHREPORT', 'PDFEXPORT')
                ORDER BY Posted_Time DESC";

            using var conn = new SqlConnection(_connectionString);
            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { ProcessId = processId });

            if (row == null) return null;

            return new DriverProcessInfo
            {
                ProcessId    = processId,
                SessionId    = row.SessionId?.ToString(),
                UserId       = row.UserId?.ToString(),
                StartTime    = row.StartTime,
                ReportName   = row.ReportName?.ToString(),
                ComputerName = row.ComputerName?.ToString()
            };
        }

        /// <summary>
        /// Returns process info for a list of PIDs in one round-trip.
        /// </summary>
        public async Task<List<DriverProcessInfo>> GetProcessInfoBatchAsync(IEnumerable<int> processIds)
        {
            var results = new List<DriverProcessInfo>();
            foreach (var pid in processIds)
            {
                var info = await GetProcessInfoAsync(pid);
                results.Add(info ?? new DriverProcessInfo { ProcessId = pid });
            }
            return results;
        }
    }
}
