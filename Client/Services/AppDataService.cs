namespace eSDSCom.Editor.Client.Services;

public class AppDataService
{
    //  this class interacts with the api (in the server projecT)  for the entire application.
    //  sending an item to the db with a complex item in it, so those complex items - XML elements - 
    //  are stored in xml datatype fields in the db. 


    private HttpClient api;
    public AppDataService(HttpClient _api)
    {
        api = _api;
    }

    private static async Task<T> ReadJsonOrThrowAsync<T>(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException(
                $"API request failed: {(int)response.StatusCode} {response.ReasonPhrase}. Body: {body}",
                inner: null,
                statusCode: response.StatusCode);
        }

        var result = await response.Content.ReadFromJsonAsync<T>();
        if (result is null)
        {
            throw new System.Text.Json.JsonException("Response body was empty or invalid JSON.");
        }

        return result;
    }


    #region Users

    public async Task<User> AddUserAsync(User user)
    {
        User newUser = new();

        try
        {
            HttpResponseMessage response = await api.PostAsJsonAsync("user/add", user);
            newUser = await ReadJsonOrThrowAsync<User>(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return newUser;
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        User newUser = new();

        try
        {
            HttpResponseMessage response = await api.PutAsJsonAsync("user/update", user);
            newUser = await ReadJsonOrThrowAsync<User>(response);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return newUser;
    }

    public async Task<User> GetUserAsync(Guid id)
    {
        User user = await api.GetFromJsonAsync<User>($"user/get?id={id}");
        return user;
    }

    public async Task<List<User>> GetUserListForOrganizationAsync(Guid orgId)
    {
        List<User> userList = await api.GetFromJsonAsync<List<User>>($"user/getfororganization?organizationId={orgId}");
        return userList;
    }


    #endregion

    #region Organizations

    public async Task<Organization> AddOrganizationAsync(Organization organization)
    {
        organization.InfoExSysString = organization.InfoExSysXDoc.OuterXml;
        organization.InfoExSysXDoc = null;

        HttpResponseMessage response = await api.PostAsJsonAsync("organization/add", organization);
        organization = await ReadJsonOrThrowAsync<Organization>(response);

        organization.InfoExSysXDoc = new();
        organization.InfoExSysXDoc.LoadXml(organization.InfoExSysString);

        return organization;
    }

    public async Task<Organization> UpdateOrganizationAsync(Organization organization)
    {
        organization.InfoExSysString = organization.InfoExSysXDoc.OuterXml;
        organization.InfoExSysXDoc = null;

        HttpResponseMessage response = await api.PutAsJsonAsync("organization/update", organization);
        organization = await ReadJsonOrThrowAsync<Organization>(response);

        organization.InfoExSysXDoc = new();
        organization.InfoExSysXDoc.LoadXml(organization.InfoExSysString);

        return organization;
    }

    public async Task<Organization> GetOrganizationAsync(Guid organizationId)
    {
        Organization org = await api.GetFromJsonAsync<Organization>($"organization/get?id={organizationId}");
        return org;
    }

    #endregion


    #region Datasheets

    public async Task<Datasheet> AddDatasheetAsync(Datasheet datasheet)
    {
        HttpResponseMessage response = await api.PostAsJsonAsync("datasheet/add", datasheet);
        Datasheet ds = await ReadJsonOrThrowAsync<Datasheet>(response);
        return ds;
    }

    public async Task<Datasheet> UpdateDatasheetAsync(Datasheet datasheet)
    {
        datasheet.DatasheetString = datasheet.DatasheetXDoc.OuterXml;
        datasheet.DatasheetXDoc = null;

        HttpResponseMessage response = await api.PutAsJsonAsync("datasheet/update", datasheet);
        Datasheet ds = await ReadJsonOrThrowAsync<Datasheet>(response);

        ds.DatasheetXDoc = new();
        ds.DatasheetXDoc.LoadXml(ds.DatasheetString);

        return ds;
    }

    public async Task<Datasheet> GetDatasheetAsync(Guid OrganizationId, Guid dsId)
    {
        Datasheet ds = await api.GetFromJsonAsync<Datasheet>($"datasheet/get?organizationId={OrganizationId}&dsId={dsId}");
        return ds;
    }

    public async Task<List<Datasheet>> GetDatasheetListForOrganizationAsync(Guid OrganizationId)
    {
        List<Datasheet> returnList = new();

        List<Datasheet> dsList = await api.GetFromJsonAsync<List<Datasheet>>($"datasheet/getfororganization?OrganizationId={OrganizationId}");

        foreach (Datasheet ds in dsList)
        {
            returnList.Add(ds);
        }

        return returnList;
    }

    public async Task<List<Datasheet>> GetDatasheetListForOrganizationAndDocumentSetAsync(Guid OrganizationId, Guid docSetId)
    {
        List<Datasheet> returnList = new();

        try
        {
            DatasheetFeed dsFeed = await GetDatasheetFeedAsync(OrganizationId, docSetId);

            List<Datasheet> dsList = new();

            //foreach (string dsId in dsFeed.DocumentIdList)
            //{
            //    Datasheet ds = await GetDatasheetAsync(OrganizationId, dsId);
            //    returnList.Add(ds);
            //}
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            throw;
        }

        return returnList;
    }



    #endregion


    #region DatasheetFeeds

    public async Task<DatasheetFeed> AddDatasheetFeedAsync(DatasheetFeed datasheetFeed)
    {
        HttpResponseMessage response = await api.PostAsJsonAsync("datasheetfeed/add", datasheetFeed);
        return await ReadJsonOrThrowAsync<DatasheetFeed>(response);
    }

    public async Task<DatasheetFeed> UpdateDatasheetFeedAsync(DatasheetFeed datasheetFeed)
    {
        HttpResponseMessage response = await api.PutAsJsonAsync("datasheetfeed/update", datasheetFeed);
        return await ReadJsonOrThrowAsync<DatasheetFeed>(response);
    }

    public async Task<DatasheetFeedItem> AddDatasheetFeedItemAsync(DatasheetFeedItem dsfi)
    {
        HttpResponseMessage response = await api.PostAsJsonAsync("datasheetfeeditem/add", dsfi);
        return await ReadJsonOrThrowAsync<DatasheetFeedItem>(response);
    }

    public async Task<DatasheetFeed> GetDatasheetFeedAsync(Guid organizationId, Guid datsheetFeedId)
    {
        return await api.GetFromJsonAsync<DatasheetFeed>($"datasheetfeed/get?organizationId={organizationId}&dsfId={datsheetFeedId}");
    }

    public async Task<List<DatasheetFeed>> GetDatasheetFeedListForOrganizationAsync(Guid OrganizationId)
    {
        return await api.GetFromJsonAsync<List<DatasheetFeed>>($"datasheetfeed/getfororganization?organizationId={OrganizationId}");
    }

    #endregion


    #region Substances

    public async Task<List<Substance>> GetAllSubstancesAsync()
    {
        return await api.GetFromJsonAsync<List<Substance>>($"substance/getall");
    }

    /// <summary>
    /// Fetch by database primary key (Substance.Id).
    /// </summary>
    public async Task<Substance> GetSubstanceAsync(Guid substanceId)
    {
        return await api.GetFromJsonAsync<Substance>($"substance/get?id={substanceId}");
    }

    /// <summary>
    /// Fetch by ECHA SubstanceId (Substance.SubstanceId).
    /// </summary>
    public async Task<Substance> GetSubstanceBySubstanceIdAsync(string substanceId)
    {
        return await api.GetFromJsonAsync<Substance>($"substance/getbysubstanceid?substanceId={Uri.EscapeDataString(substanceId)}");
    }

    #endregion
}

