using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace SurveyonApi.Services
{
    public enum DbType
    {
        PostgreSql,
        MsSql
    }

    public interface IDbService
    {
        IDbConnection GetConnection(DbType dbType);
        Task<IEnumerable<T>> QueryAsync<T>(DbType dbType, string sql, object? param = null);
        Task<int> ExecuteAsync(DbType dbType, string sql, object? param = null);
        Task<T?> QueryFirstOrDefaultAsync<T>(DbType dbType, string sql, object? param = null);
    }

    public class DbService : IDbService
    {
        private readonly IConfiguration _configuration;

        public DbService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IDbConnection GetConnection(DbType dbType)
        {
            string? connectionString;
            IDbConnection connection;

            switch (dbType)
            {
                case DbType.PostgreSql:
                    connectionString = _configuration.GetConnectionString("Postgres");
                    connection = new NpgsqlConnection(connectionString);
                    break;
                case DbType.MsSql:
                    connectionString = _configuration.GetConnectionString("SqlServer");
                    connection = new SqlConnection(connectionString);
                    break;
                default:
                    throw new ArgumentException("유효하지 않은 DbType입니다");
            }

            return connection;
        }

        public async Task<IEnumerable<T>> QueryAsync<T>(DbType dbType, string sql, object? param = null)
        {
            using var connection = GetConnection(dbType);
            return await connection.QueryAsync<T>(sql, param);
        }

        public async Task<int> ExecuteAsync(DbType dbType, string sql, object? param = null)
        {
            using var connection = GetConnection(dbType);
            return await connection.ExecuteAsync(sql, param);
        }

        public async Task<T?> QueryFirstOrDefaultAsync<T>(DbType dbType, string sql, object? param = null)
        {
            using var connection = GetConnection(dbType);
            return await connection.QueryFirstOrDefaultAsync<T>(sql, param);
        }
    }
}
