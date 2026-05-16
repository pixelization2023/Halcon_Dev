using System.Data;
using Inspection.Models;
using MySql.Data.MySqlClient;
using Serilog;

namespace Inspection.Services
{
    /// <summary>
    /// 检测结果持久化（MySQL）。
    /// 迁移自 窗体.Mysql.MyDbcontext（EF Core + MySql.EntityFrameworkCore）。
    /// 改用 MySql.Data 直接执行参数化 SQL：依赖更轻、启动更快，并保留原有表名/列名
    /// （tb_data1 / tb_data2，含中文列 PCS号），现场库可直接复用。
    /// </summary>
    public class InspectionResultStore
    {
        /// <summary>反引号：用于给可能包含中文/保留字的列名加引号</summary>
        private const string Q = "\u0060";

        private readonly ILogger _logger;

        public InspectionResultStore(ILogger logger)
        {
            _logger = logger.ForContext<InspectionResultStore>();
        }

        /// <summary>数据库配置（切换产品时通过 <see cref="Configure"/> 更新）</summary>
        public MySqlSettings Configuration { get; private set; } = new();

        public void Configure(MySqlSettings parameter)
        {
            Configuration = parameter ?? new MySqlSettings();
        }

        /// <summary>构造连接串（原 MyDbcontext.OnConfiguring）</summary>
        public string BuildConnectionString()
        {
            var c = Configuration;
            return $"Server={c.Ip};Port={c.Port};Database={c.DatabaseName};User ID={c.UserName};" +
                   $"Password={c.Password};CharSet=utf8mb4;Allow User Variables=true;Connection Timeout=5;";
        }

        private MySqlConnection CreateConnection() => new(BuildConnectionString());

        #region 连接与建表

        /// <summary>测试数据库连通性（原 MyDbcontext.IsDatabaseConnected）</summary>
        public bool TestConnection()
        {
            try
            {
                using var conn = CreateConnection();
                conn.Open();
                return conn.State == ConnectionState.Open;
            }
            catch (Exception ex)
            {
                _logger.Warning("数据库连接失败: {Message}", ex.Message);
                return false;
            }
        }

        /// <summary>按需建表（原 EF Core 的动态建表行为）</summary>
        public bool EnsureSchema()
        {
            var resultTable = Configuration.ResultTable;
            var codeTable = Configuration.CodeTable;

            try
            {
                using var conn = CreateConnection();
                conn.Open();

                var ddlResult =
                    $"CREATE TABLE IF NOT EXISTS {Q}{resultTable}{Q} (" +
                    $"{Q}indenx{Q} INT NOT NULL AUTO_INCREMENT, " +
                    $"{Q}DetaTime{Q} DATETIME NULL, " +
                    $"{Q}PhotoName{Q} TEXT NULL, " +
                    $"{Q}PCS号{Q} TEXT NULL, " +
                    $"{Q}PaperCode{Q} TEXT NULL, " +
                    $"{Q}LaserCode{Q} TEXT NULL, " +
                    $"{Q}Lot{Q} TEXT NULL, " +
                    $"{Q}UserID{Q} TEXT NULL, " +
                    $"{Q}Item{Q} TEXT NULL, " +
                    $"{Q}Model{Q} TEXT NULL, " +
                    $"{Q}PointSet{Q} TEXT NULL, " +
                    $"{Q}Result{Q} TEXT NULL, " +
                    $"PRIMARY KEY ({Q}indenx{Q})" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

                var ddlCode =
                    $"CREATE TABLE IF NOT EXISTS {Q}{codeTable}{Q} (" +
                    $"{Q}indenx{Q} INT NOT NULL AUTO_INCREMENT, " +
                    $"{Q}Code{Q} TEXT NULL, " +
                    $"{Q}DetaTime{Q} DATETIME NULL, " +
                    $"PRIMARY KEY ({Q}indenx{Q})" +
                    ") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;";

                using (var cmd = new MySqlCommand(ddlResult, conn)) cmd.ExecuteNonQuery();
                using (var cmd = new MySqlCommand(ddlCode, conn)) cmd.ExecuteNonQuery();

                _logger.Information("数据库表已就绪: {ResultTable} / {CodeTable}", resultTable, codeTable);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "创建数据库表失败");
                return false;
            }
        }

        #endregion

        #region 写入

        /// <summary>写入一条 PCS 检测结果（原 tb_data1.Add + SaveChanges）</summary>
        public bool InsertResult(InspectionRecord record)
        {
            var table = Configuration.ResultTable;

            const string sqlTemplate =
                "INSERT INTO {0}{1}{0} ({0}DetaTime{0},{0}PhotoName{0},{0}PCS号{0},{0}PaperCode{0},{0}LaserCode{0}," +
                "{0}Lot{0},{0}UserID{0},{0}Item{0},{0}Model{0},{0}PointSet{0},{0}Result{0}) " +
                "VALUES (@time,@photo,@pcs,@paper,@laser,@lot,@user,@item,@model,@points,@result);";

            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = new MySqlCommand(string.Format(sqlTemplate, Q, table), conn);

                cmd.Parameters.AddWithValue("@time", record.RecordTime);
                cmd.Parameters.AddWithValue("@photo", record.PhotoName ?? string.Empty);
                cmd.Parameters.AddWithValue("@pcs", record.PcsNumber ?? string.Empty);
                cmd.Parameters.AddWithValue("@paper", record.PaperCode ?? string.Empty);
                cmd.Parameters.AddWithValue("@laser", record.LaserCode ?? string.Empty);
                cmd.Parameters.AddWithValue("@lot", record.Lot ?? string.Empty);
                cmd.Parameters.AddWithValue("@user", record.UserId ?? string.Empty);
                cmd.Parameters.AddWithValue("@item", record.Item ?? string.Empty);
                cmd.Parameters.AddWithValue("@model", record.Model ?? string.Empty);
                cmd.Parameters.AddWithValue("@points", record.PointSet ?? string.Empty);
                cmd.Parameters.AddWithValue("@result", record.Result ?? string.Empty);

                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "写入检测结果失败: PCS={Pcs}", record.PcsNumber);
                return false;
            }
        }

        /// <summary>写入一条条码记录（原 tb_data2.Add + SaveChanges）</summary>
        public bool InsertCode(string code)
        {
            var table = Configuration.CodeTable;
            var sql = $"INSERT INTO {Q}{table}{Q} ({Q}Code{Q},{Q}DetaTime{Q}) VALUES (@code,@time);";

            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@code", code ?? string.Empty);
                cmd.Parameters.AddWithValue("@time", DateTime.Now);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "写入条码失败: {Code}", code);
                return false;
            }
        }

        #endregion

        #region 查询（迁移自 UploadMethod.QueryTheData）

        private List<InspectionRecord> Query(string where, params (string Name, object? Value)[] parameters)
        {
            var table = Configuration.ResultTable;
            var sql = $"SELECT {Q}indenx{Q},{Q}DetaTime{Q},{Q}PhotoName{Q},{Q}PCS号{Q},{Q}PaperCode{Q},{Q}LaserCode{Q}," +
                      $"{Q}Lot{Q},{Q}UserID{Q},{Q}Item{Q},{Q}Model{Q},{Q}PointSet{Q},{Q}Result{Q} " +
                      $"FROM {Q}{table}{Q}" + (string.IsNullOrWhiteSpace(where) ? string.Empty : " WHERE " + where) +
                      $" ORDER BY {Q}indenx{Q} DESC LIMIT 5000;";

            var list = new List<InspectionRecord>();

            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = new MySqlCommand(sql, conn);
                foreach (var (name, value) in parameters)
                    cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new InspectionRecord
                    {
                        Index = reader.GetInt32(0),
                        RecordTime = reader.IsDBNull(1) ? DateTime.MinValue : reader.GetDateTime(1),
                        PhotoName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        PcsNumber = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                        PaperCode = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
                        LaserCode = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
                        Lot = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
                        UserId = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                        Item = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
                        Model = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                        PointSet = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                        Result = reader.IsDBNull(11) ? string.Empty : reader.GetString(11)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "查询检测结果失败");
            }

            return list;
        }

        /// <summary>按时间范围查询（原 QueryTheData(DateTimePicker, DateTimePicker, DataGridView)）</summary>
        public List<InspectionRecord> QueryByTime(DateTime from, DateTime to)
            => Query($"{Q}DetaTime{Q} BETWEEN @from AND @to", ("@from", from), ("@to", to));

        /// <summary>按 LOT 查询（原 QueryTheData(TextBox "lot", ...)）</summary>
        public List<InspectionRecord> QueryByLot(string lot)
            => Query($"{Q}Lot{Q} = @lot", ("@lot", lot));

        /// <summary>按镭射码查询</summary>
        public List<InspectionRecord> QueryByLaserCode(string laserCode)
            => Query($"{Q}LaserCode{Q} = @code", ("@code", laserCode));

        /// <summary>查询全部（原 "查询所有数据"）</summary>
        public List<InspectionRecord> QueryAll() => Query(string.Empty);

        /// <summary>按主键删除（原 "删除数据"）</summary>
        public bool DeleteById(int id)
        {
            var table = Configuration.ResultTable;
            var sql = $"DELETE FROM {Q}{table}{Q} WHERE {Q}indenx{Q} = @id;";

            try
            {
                using var conn = CreateConnection();
                conn.Open();
                using var cmd = new MySqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "删除记录失败: {Id}", id);
                return false;
            }
        }

        #endregion
    }
}
