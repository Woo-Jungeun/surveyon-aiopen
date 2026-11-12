using System.Data;
using Microsoft.Data.SqlClient;
using Dapper;

namespace SurveyonApi.Services
{
    public interface IMsSqlLib
    {
        DataTable ExecuteQuery(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer");
        int ExecuteNonQuery(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer");
        object? ExecuteScalar(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer");

        // Async Methods
        Task<DataTable> ExecuteQueryAsync(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer", CancellationToken ct = default);
        Task<int> ExecuteNonQueryAsync(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer", CancellationToken ct = default);
        Task<object?> ExecuteScalarAsync(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer", CancellationToken ct = default);

        // Dapper Async Methods
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default);
        Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default);
        Task<int> ExecuteAsync(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default);
        Task<T> ExecuteScalarAsync<T>(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default);
    }

    public class MsSqlLib : IMsSqlLib
    {
        private readonly IConfiguration _configuration;

        public MsSqlLib(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private SqlConnection GetConnection(string connectionKey)
        {
            string connectionString = _configuration.GetConnectionString(connectionKey)
                ?? throw new InvalidOperationException($"Connection string '{connectionKey}' not found.");
            return new SqlConnection(connectionString);
        }

        public DataTable ExecuteQuery(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer")
        {
            using var connection = GetConnection(connectionKey);
            using var command = new SqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            var dataTable = new DataTable();
            using var adapter = new SqlDataAdapter(command);

            connection.Open();
            adapter.Fill(dataTable);

            return dataTable;
        }

        public int ExecuteNonQuery(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer")
        {
            using var connection = GetConnection(connectionKey);
            using var command = new SqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            connection.Open();
            return command.ExecuteNonQuery();
        }

        public object? ExecuteScalar(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer")
        {
            using var connection = GetConnection(connectionKey);
            using var command = new SqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            connection.Open();
            return command.ExecuteScalar();
        }

        public async Task<DataTable> ExecuteQueryAsync(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            using var command = new SqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            DataTable dataTable = new DataTable();
            await connection.OpenAsync(ct);
            using var reader = await command.ExecuteReaderAsync(ct);
            dataTable.Load(reader);

            return dataTable;
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            using var command = new SqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            await connection.OpenAsync(ct);
            return await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<object?> ExecuteScalarAsync(string sql, List<SqlParameter>? parameters = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            using var command = new SqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            await connection.OpenAsync(ct);
            return await command.ExecuteScalarAsync(ct);
        }

        // Dapper Async Implementations
        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        }

        public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.QueryFirstOrDefaultAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        }

        public async Task<int> ExecuteAsync(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null, string connectionKey = "SqlServer", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        }
    }
}
