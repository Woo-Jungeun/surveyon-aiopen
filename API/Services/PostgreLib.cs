using System.Data;
using Npgsql;
using Dapper;

namespace SurveyonApi.Services
{
    public interface IPostgreLib
    {
        DataTable ExecuteQuery(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres");
        int ExecuteNonQuery(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres");
        object? ExecuteScalar(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres");

        // Async Methods
        Task<DataTable> ExecuteQueryAsync(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres", CancellationToken ct = default);
        Task<int> ExecuteNonQueryAsync(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres", CancellationToken ct = default);
        Task<object?> ExecuteScalarAsync(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres", CancellationToken ct = default);

        // Dapper Async Methods
        Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default);
        Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default);
        Task<int> ExecuteAsync(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default);
        Task<T> ExecuteScalarAsync<T>(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default);
    }

    public class PostgreLib : IPostgreLib
    {
        private readonly IConfiguration _configuration;

        public PostgreLib(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private NpgsqlConnection GetConnection(string connectionKey)
        {
            string connectionString = _configuration.GetConnectionString(connectionKey)
                ?? throw new InvalidOperationException($"Connection string '{connectionKey}' not found.");
            return new NpgsqlConnection(connectionString);
        }

        public DataTable ExecuteQuery(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres")
        {
            using var connection = GetConnection(connectionKey);
            using var command = new NpgsqlCommand(sql, connection);
            
            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            var dataTable = new DataTable();
            using var adapter = new NpgsqlDataAdapter(command);
            
            connection.Open();
            adapter.Fill(dataTable);
            
            return dataTable;
        }

        public int ExecuteNonQuery(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres")
        {
            using var connection = GetConnection(connectionKey);
            using var command = new NpgsqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            connection.Open();
            return command.ExecuteNonQuery();
        }

        public object? ExecuteScalar(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres")
        {
            using var connection = GetConnection(connectionKey);
            using var command = new NpgsqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            connection.Open();
            return command.ExecuteScalar();
        }

        public async Task<DataTable> ExecuteQueryAsync(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            using var command = new NpgsqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            var dataTable = new DataTable();
            await connection.OpenAsync(ct);
            using var reader = await command.ExecuteReaderAsync(ct);
            dataTable.Load(reader);

            return dataTable;
        }

        public async Task<int> ExecuteNonQueryAsync(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            using var command = new NpgsqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            await connection.OpenAsync(ct);
            return await command.ExecuteNonQueryAsync(ct);
        }

        public async Task<object?> ExecuteScalarAsync(string sql, List<NpgsqlParameter>? parameters = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            using var command = new NpgsqlCommand(sql, connection);

            if (parameters != null)
            {
                command.Parameters.AddRange(parameters.ToArray());
            }

            await connection.OpenAsync(ct);
            return await command.ExecuteScalarAsync(ct);
        }

        // Dapper Async Implementations
        public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return (await connection.QueryAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct))).ToList();
        }

        public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.QueryFirstOrDefaultAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        }

        public async Task<int> ExecuteAsync(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.ExecuteAsync(new CommandDefinition(sql, param, cancellationToken: ct));
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object? param = null, string connectionKey = "Postgres", CancellationToken ct = default)
        {
            using var connection = GetConnection(connectionKey);
            return await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, param, cancellationToken: ct));
        }
    }
}
