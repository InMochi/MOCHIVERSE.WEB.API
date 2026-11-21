using System.Data;
using Microsoft.Data.SqlClient;

namespace MOCHI.VERSE.API.Class;

/// <summary>
/// Async ADO.NET SQL Server helper with explicit parameterization and transaction support.
/// </summary>
public sealed class SqlHelper : IDisposable, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlCommand _command;

    public SqlTransaction? Transaction { get; private set; }

    public string CommandText
    {
        get => _command.CommandText;
        set => _command.CommandText = value;
    }

    public CommandType CommandType
    {
        get => _command.CommandType;
        set => _command.CommandType = value;
    }

    public int CommandTimeout
    {
        get => _command.CommandTimeout;
        set => _command.CommandTimeout = value;
    }

    public SqlHelper(IConfiguration configuration, string connectionName = "DefaultConnection")
        : this(ResolveConnectionString(configuration, connectionName))
    {
    }

    public SqlHelper(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string cannot be empty.");
        }

        _connection = new SqlConnection(connectionString);
        _command = new SqlCommand
        {
            Connection = _connection,
            CommandType = CommandType.Text
        };
    }

    private static string ResolveConnectionString(IConfiguration configuration, string connectionName)
    {
        var connectionString = configuration.GetConnectionString(connectionName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Connection string '{connectionName}' is not configured.");
        }

        return connectionString;
    }

    public void AddParameter(
        string paramName,
        object? value,
        SqlDbType dbType,
        int size = 0,
        ParameterDirection direction = ParameterDirection.Input)
    {
        var parameter = new SqlParameter(paramName, dbType)
        {
            Value = value ?? DBNull.Value,
            Direction = direction
        };

        if (size > 0)
        {
            parameter.Size = size;
        }

        _command.Parameters.Add(parameter);
    }

    public void ClearParameters() => _command.Parameters.Clear();

    public object? GetParameterValue(string paramName) => _command.Parameters[paramName]?.Value;

    public Task<int> ExecuteNonQueryAsync(CancellationToken cancellationToken = default) =>
        ExecuteWithConnectionAsync(
            () => _command.ExecuteNonQueryAsync(cancellationToken),
            cancellationToken);

    public Task<object?> ExecuteScalarAsync(CancellationToken cancellationToken = default) =>
        ExecuteWithConnectionAsync(
            () => _command.ExecuteScalarAsync(cancellationToken),
            cancellationToken);

    public async Task<SqlDataReader> ExecuteReaderAsync(
        CommandBehavior behavior = CommandBehavior.Default,
        CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        var effectiveBehavior = Transaction is null
            ? behavior | CommandBehavior.CloseConnection
            : behavior;

        return await _command.ExecuteReaderAsync(effectiveBehavior, cancellationToken);
    }

    public Task<T?> QueryFirstOrDefaultAsync<T>(
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken = default) =>
        ExecuteWithConnectionAsync(async () =>
        {
            await using var reader = await _command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            return await reader.ReadAsync(cancellationToken) ? map(reader) : default;
        }, cancellationToken);

    public Task<List<T>> QueryListAsync<T>(
        Func<SqlDataReader, T> map,
        CancellationToken cancellationToken = default) =>
        ExecuteWithConnectionAsync(async () =>
        {
            var results = new List<T>();
            await using var reader = await _command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                results.Add(map(reader));
            }
            return results;
        }, cancellationToken);

    public async Task BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        if (_connection.State != ConnectionState.Open)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        Transaction = (SqlTransaction)await _connection.BeginTransactionAsync(isolationLevel, cancellationToken);
        _command.Transaction = Transaction;
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is not null)
        {
            await Transaction.CommitAsync(cancellationToken);
        }

        await CleanupTransactionAsync();
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (Transaction is not null)
        {
            await Transaction.RollbackAsync(cancellationToken);
        }

        await CleanupTransactionAsync();
    }

    private async Task<T> ExecuteWithConnectionAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken)
    {
        var ownsConnection = Transaction is null && _connection.State != ConnectionState.Open;
        if (ownsConnection)
        {
            await _connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await operation();
        }
        finally
        {
            if (ownsConnection && _connection.State == ConnectionState.Open)
            {
                await _connection.CloseAsync();
            }
        }
    }

    private async Task CleanupTransactionAsync()
    {
        if (Transaction is not null)
        {
            await Transaction.DisposeAsync();
            Transaction = null;
        }

        _command.Transaction = null;

        if (_connection.State == ConnectionState.Open)
        {
            await _connection.CloseAsync();
        }
    }

    public void Dispose()
    {
        Transaction?.Dispose();
        _command.Dispose();
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (Transaction is not null)
        {
            await Transaction.DisposeAsync();
        }

        await _command.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public static string ConvertDbNullToBlank(object? value) =>
        value is null or DBNull ? string.Empty : value.ToString() ?? string.Empty;

    public static object ConvertStringBlankToDbNull(string? value) =>
        string.IsNullOrEmpty(value) ? DBNull.Value : value;
}
