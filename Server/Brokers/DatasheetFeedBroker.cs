namespace eSDSCom.Editor.Server.Brokers;

using NpgsqlTypes;

public interface IDatasheetFeedBroker
{
    Task<DatasheetFeed> Get(Guid organizationId, Guid dsfId);
    Task<List<DatasheetFeed>> GetForOrganization(Guid organizationId);
    Task<List<DatasheetFeed>> GetForUser(Guid organizationId, Guid userId);
    Task<DatasheetFeed> Add(DatasheetFeed sheet);
    Task<DatasheetFeed> Update(DatasheetFeed sheet);
    Task<bool> Delete(Guid dsfId);
}

public class DatasheetFeedBroker :  IDatasheetFeedBroker
{
    private static string ConnectionString;

    private static NpgsqlParameter CreateParam(object? value, NpgsqlDbType dbType)
    {
        return new NpgsqlParameter
        {
            NpgsqlDbType = dbType,
            Value = value ?? DBNull.Value
        };
    }

    public DatasheetFeedBroker(string connString) 
    {
        ConnectionString = connString;
    }   

    public async Task<DatasheetFeed> Get(Guid organizationId, Guid dsfId)
    {
        DatasheetFeed dsf = new();

        try
        {
            using NpgsqlConnection dbConn = new(ConnectionString);
            string sql = @"SELECT dsf.*, u.NAME as USERNAME 
                            FROM DATASHEETFEEDS dsf , USERS u
                            WHERE dsf.UserId = u.ID
                            AND dsf.ID = $1
                            AND dsf.ORGANIZATIONID = $2 ";

            using NpgsqlCommand cmd = new(sql, dbConn)
            {
                Parameters =
                {
                    new() { Value = dsfId },
                    new() { Value = organizationId }
                }
            };

            dbConn.Open();
            NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();

            while (reader.Read())
            {
                dsf = DBUtils.GetDatasheetFeedFromReader(reader);
            }

            await reader.CloseAsync();
            await dbConn.CloseAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            throw;
        }

        return dsf;
    }


    public async Task<List<DatasheetFeed>> GetForOrganization(Guid organizationId)
    {
        List<DatasheetFeed> dsfList = new();

        try
        {
            using NpgsqlConnection dbConn = new(ConnectionString);
            string sql = @"SELECT dsf.*, u.NAME as USERNAME
                            FROM DATASHEETFEEDS dsf , USERS u
                            WHERE dsf.UserId = u.ID
                            AND dsf.ORGANIZATIONID =  $1";

            using NpgsqlCommand cmd = new(sql, dbConn)
            {
                Parameters =
                {
                    new() { Value = organizationId }
                }
            };

            dbConn.Open();
            NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();

            while (reader.Read())
            {
                dsfList.Add(DBUtils.GetDatasheetFeedFromReader(reader));
            }
            await reader.CloseAsync();
            await dbConn.CloseAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            throw;
        }

        return dsfList;
    }

    public async Task<List<DatasheetFeed>> GetForUser(Guid organizationId, Guid userId)
    {
        List<DatasheetFeed> dsfList = new();

        try
        {
            using NpgsqlConnection dbConn = new(ConnectionString);
            string sql = @"SELECT dsf.*, u.NAME as USERNAME
                            FROM DATASHEETFEEDS dsf , USERS u
                            WHERE dsf.UserId = u.ID
                            AND dsf.ORGANIZATIONID = $1
                            AND dsf.USERID = $2 ";

            using NpgsqlCommand cmd = new(sql, dbConn)
            {
                Parameters =
                {
                    new() { Value = organizationId },
                    new() { Value = userId }
                }
            };

            dbConn.Open();
            NpgsqlDataReader reader = await cmd.ExecuteReaderAsync();

            while (reader.Read())
            {
                dsfList.Add(DBUtils.GetDatasheetFeedFromReader(reader));
            }
            await reader.CloseAsync();
            await dbConn.CloseAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            throw;
        }

        return dsfList;
    }

    public async Task<DatasheetFeed> Add(DatasheetFeed dsf)
    {
        try
        {
            if (dsf.Id == default)
            {
                dsf.Id = Guid.NewGuid();
            }

            using NpgsqlConnection dbConn = new(ConnectionString);
            string sql = @"INSERT INTO DatasheetFeeds
                                (ID, ORGANIZATIONID, USERID, NAME, DATASHEETFEEDDOC, COMMENTS, STATUS)
                            VALUES
                                ($1, $2, $3, $4, $5, $6, $7) ";

            using NpgsqlCommand cmd = new(sql, dbConn);
            // For positional placeholders ($1, $2, ...), Npgsql binds parameters by order.
            // Keep parameters unnamed and add them in the same order as the placeholders.
            cmd.Parameters.Add(CreateParam(dsf.Id, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(CreateParam(dsf.OrganizationId, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(CreateParam(dsf.UserId, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(CreateParam(dsf.Name, NpgsqlDbType.Text));
            cmd.Parameters.Add(CreateParam(dsf.DatasheetFeedString, NpgsqlDbType.Text));
            cmd.Parameters.Add(CreateParam(dsf.Comments, NpgsqlDbType.Text));
            cmd.Parameters.Add(CreateParam(dsf.Status, NpgsqlDbType.Integer));

            dbConn.Open();
            int result = await cmd.ExecuteNonQueryAsync();
            await dbConn.CloseAsync();
            if (result > 0)
            {
                return dsf;
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            throw;
        }
    }

    public async Task<DatasheetFeed> Update(DatasheetFeed dsf)
    {
        try
        {
            using NpgsqlConnection dbConn = new(ConnectionString);
            string sql = @"UPDATE DatasheetFeeds
                            SET ORGANIZATIONID = $1, 
                                USERID = $2, 
                                NAME = $3,                                
                                DATASHEETFEEDDOC = $4,
                                COMMENTS =$5,
                                STATUS = $6
                            WHERE ID = $7 ";

            using NpgsqlCommand cmd = new(sql, dbConn);
            cmd.Parameters.Add(CreateParam(dsf.OrganizationId, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(CreateParam(dsf.UserId, NpgsqlDbType.Uuid));
            cmd.Parameters.Add(CreateParam(dsf.Name, NpgsqlDbType.Text));
            cmd.Parameters.Add(CreateParam(dsf.DatasheetFeedString, NpgsqlDbType.Text));
            cmd.Parameters.Add(CreateParam(dsf.Comments, NpgsqlDbType.Text));
            cmd.Parameters.Add(CreateParam(dsf.Status, NpgsqlDbType.Integer));
            cmd.Parameters.Add(CreateParam(dsf.Id, NpgsqlDbType.Uuid));

          

            dbConn.Open();
            int result = await cmd.ExecuteNonQueryAsync();
            await dbConn.CloseAsync();

            if (result > 0)
            {
                return dsf;
            }
            else
            {
                return null;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
            throw;
        }
    }

    public async Task<bool> Delete(Guid dsfId)
    {
        int result = 0;
        try
        {
            using NpgsqlConnection dbConn = new(ConnectionString);
            string sql = "DELETE FROM DATASHEETFEEDS WHERE ID = $1";
            using NpgsqlCommand cmd = new(sql, dbConn)
            {
                Parameters =
                {
                    new() { Value = dsfId }                  
                }
            };

            dbConn.Open();
            result = await cmd.ExecuteNonQueryAsync();
            await dbConn.CloseAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex.Message);
        }
        return result > 0;
    }
}

