namespace eSDSCom.Editor.Tests;

public class DatabaseFixture : BaseTestData, IDisposable
{
    private static readonly object InitLock = new();
    private static bool IsInitialized;

    public DatabaseFixture()
    {
        EnsureInitialized();
    }

    private static void EnsureInitialized()
    {
        lock (InitLock)
        {
            if (IsInitialized)
            {
                return;
            }

            using var conn = new NpgsqlConnection(TestConnectionString);
            conn.Open();

            EnsureTestSchema(conn);
            EnsureSchema(conn);

            // Start from a clean state so test expectations are deterministic regardless of existing DB contents.
            // With Search Path forced to a dedicated schema, this is safe and fast.
            ExecuteNonQuery(conn, "TRUNCATE TABLE phrases, substances;");

            SeedReferenceData(conn);

            IsInitialized = true;
        }
    }

    private static void EnsureTestSchema(NpgsqlConnection conn)
    {
        // Ensure our test schema exists. Resolution of unqualified names is controlled by `Search Path`
        // (which BaseTestData enforces for ESDSCOM_TEST_CONNECTION_STRING). We still create the schema
        // defensively in case the DB is brand-new.
        ExecuteNonQuery(conn, "CREATE SCHEMA IF NOT EXISTS esdscom_tests;");
    }

    private static void EnsureSchema(NpgsqlConnection conn)
    {
        // Minimal schema required for broker integration tests.
        // We intentionally avoid foreign keys here to keep tests lightweight and order-independent.

        ExecuteNonQuery(conn, @"
            CREATE TABLE IF NOT EXISTS users (
                id uuid PRIMARY KEY,
                organizationid uuid NOT NULL,
                email text,
                name text,
                role integer NOT NULL DEFAULT 0,
                isactive boolean NOT NULL DEFAULT true,
                createddate timestamptz NOT NULL DEFAULT now(),
                updateddate timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS organizations (
                id uuid PRIMARY KEY,
                organizationtype text,
                name text,
                address text,
                informationfromexportingsystem text,
                createddate timestamptz NOT NULL DEFAULT now(),
                updateddate timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS datasheets (
                id uuid PRIMARY KEY,
                organizationid uuid,
                userid uuid,
                name text,
                createddate timestamptz NOT NULL DEFAULT now(),
                updateddate timestamptz NOT NULL DEFAULT now(),
                status integer NOT NULL DEFAULT 0,
                datasheetdoc text,
                comments text,
                regionsstring text,
                materialtype text
            );

            CREATE TABLE IF NOT EXISTS datasheetfeeds (
                id uuid PRIMARY KEY,
                organizationid uuid,
                userid uuid,
                name text,
                createddate timestamptz NOT NULL DEFAULT now(),
                updateddate timestamptz NOT NULL DEFAULT now(),
                datasheetfeeddoc text,
                comments text,
                status integer NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS datasheetfeeditems (
                datasheetfeedid uuid NOT NULL,
                datasheetid uuid NOT NULL,
                userid uuid,
                createddate timestamptz NOT NULL DEFAULT now(),
                updateddate timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (datasheetfeedid, datasheetid)
            );

            CREATE TABLE IF NOT EXISTS phrases (
                id uuid PRIMARY KEY,
                struccode text NOT NULL,
                xpath text,
                region text,
                origcode text,
                english text,
                german text,
                revdate text,
                source text,
                info text,
                origin text
            );

            -- Unique index is sufficient for ON CONFLICT (struccode) and is schema-local.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_phrases_struccode ON phrases (struccode);

            CREATE TABLE IF NOT EXISTS substances (
                id uuid PRIMARY KEY,
                substanceid text NOT NULL,
                name text,
                ecnumber text,
                casnumber text,
                registrationstatus text,
                registrationtype text,
                submissiontype text,
                tonnageband text,
                tonnagebandmin text,
                tonnagebandmax text,
                lastupdated text,
                factsheeturl text,
                substanceinformationpage text
            );

            -- Unique index is sufficient for ON CONFLICT (substanceid) and is schema-local.
            CREATE UNIQUE INDEX IF NOT EXISTS ux_substances_substanceid ON substances (substanceid);
        ");
    }

    private static void SeedReferenceData(NpgsqlConnection conn)
    {
        // Keep this deterministic and minimal so a fresh empty DB is enough to run tests.
        SeedPhrases(conn);
        SeedSubstances(conn);
    }

    private static void SeedPhrases(NpgsqlConnection conn)
    {
        // 1) The exact phrase used by BaseTestData.GetTestPhrase()
        UpsertPhrase(
            conn,
            id: Guid.Parse("b2da6c6c-4769-4f52-8b5b-3d1b1ea0b2e5"),
            strucCode: "01.01.02.02.00.1006",
            xPath: "//DatasheetFeed/Datasheet/IdentificationSubstPrep/RelevantIdentifiedUse",
            region: "EU",
            origCode: "",
            english: "Aerating and dearating agents",
            german: "Belüftungs- und Entlüftungsmittel",
            revDate: "01/03/17",
            source: "Table R.12- 15: Descriptor list for Technical functions (TF)",
            info: "Substance that influences the amount of air or gases entrained in a material.",
            origin: "Core");

        // 2) Exactly 10 phrases with a shared prefix for GetListByPrefixTest
        const string prefix = "01.01.03.00";
        for (int i = 1; i <= 10; i++)
        {
            string code = $"{prefix}.{i:0000}";
            UpsertPhrase(
                conn,
                id: Guid.Parse($"00000000-0000-0000-0000-{i:000000000000}"),
                strucCode: code,
                xPath: "",
                region: "EU",
                origCode: "",
                english: $"Test phrase {i}",
                german: $"Testphrase {i}",
                revDate: "",
                source: "",
                info: "",
                origin: "Test");
        }
    }

    private static void UpsertPhrase(
        NpgsqlConnection conn,
        Guid id,
        string strucCode,
        string xPath,
        string region,
        string origCode,
        string english,
        string german,
        string revDate,
        string source,
        string info,
        string origin)
    {
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO phrases
                (id, struccode, xpath, region, origcode, english, german, revdate, source, info, origin)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
            ON CONFLICT (struccode)
            DO UPDATE SET
                xpath = EXCLUDED.xpath,
                region = EXCLUDED.region,
                origcode = EXCLUDED.origcode,
                english = EXCLUDED.english,
                german = EXCLUDED.german,
                revdate = EXCLUDED.revdate,
                source = EXCLUDED.source,
                info = EXCLUDED.info,
                origin = EXCLUDED.origin;
        ", conn)
        {
            Parameters =
            {
                new() { Value = id },
                new() { Value = strucCode },
                new() { Value = xPath },
                new() { Value = region },
                new() { Value = origCode },
                new() { Value = english },
                new() { Value = german },
                new() { Value = revDate },
                new() { Value = source },
                new() { Value = info },
                new() { Value = origin }
            }
        };

        cmd.ExecuteNonQuery();
    }

    private static void SeedSubstances(NpgsqlConnection conn)
    {
        // Deterministic minimal substance set for SubstanceBrokerTests
        UpsertSubstance(conn,
            id: Guid.Parse("46e1be11-4eba-4c5f-a967-18db3c97f1e3"),
            substanceId: "100.003.133",
            name: "1-bromopropane",
            ecNumber: "203-445-0",
            casNumber: "106-94-5",
            registrationStatus: "Active",
            registrationType: "INTERMEDIATE",
            submissionType: "INDIVIDUAL_SUBMISSION",
            tonnageBand: "Intermediate use only",
            tonnageBandMin: "",
            tonnageBandMax: "",
            lastUpdated: "07-07-2015",
            factsheetUrl: "https://echa.europa.eu/registration-dossier/-/registered-dossier/6200",
            substanceInformationPage: "");

        UpsertSubstance(conn,
            id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            substanceId: "100.337.580",
            name: "Test substance A",
            ecNumber: "000-000-0",
            casNumber: "000-00-0",
            registrationStatus: "Active",
            registrationType: "TEST",
            submissionType: "TEST",
            tonnageBand: "",
            tonnageBandMin: "",
            tonnageBandMax: "",
            lastUpdated: "",
            factsheetUrl: "",
            substanceInformationPage: "");

        UpsertSubstance(conn,
            id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            substanceId: "100.101.684",
            name: "Test substance B",
            ecNumber: "111-111-1",
            casNumber: "111-11-1",
            registrationStatus: "Active",
            registrationType: "TEST",
            submissionType: "TEST",
            tonnageBand: "",
            tonnageBandMin: "",
            tonnageBandMax: "",
            lastUpdated: "",
            factsheetUrl: "",
            substanceInformationPage: "");
    }

    private static void UpsertSubstance(
        NpgsqlConnection conn,
        Guid id,
        string substanceId,
        string name,
        string ecNumber,
        string casNumber,
        string registrationStatus,
        string registrationType,
        string submissionType,
        string tonnageBand,
        string tonnageBandMin,
        string tonnageBandMax,
        string lastUpdated,
        string factsheetUrl,
        string substanceInformationPage)
    {
        using var cmd = new NpgsqlCommand(@"
            INSERT INTO substances
                (id, substanceid, name, ecnumber, casnumber, registrationstatus, registrationtype,
                 submissiontype, tonnageband, tonnagebandmin, tonnagebandmax, lastupdated,
                 factsheeturl, substanceinformationpage)
            VALUES
                ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
            ON CONFLICT (substanceid)
            DO UPDATE SET
                name = EXCLUDED.name,
                ecnumber = EXCLUDED.ecnumber,
                casnumber = EXCLUDED.casnumber,
                registrationstatus = EXCLUDED.registrationstatus,
                registrationtype = EXCLUDED.registrationtype,
                submissiontype = EXCLUDED.submissiontype,
                tonnageband = EXCLUDED.tonnageband,
                tonnagebandmin = EXCLUDED.tonnagebandmin,
                tonnagebandmax = EXCLUDED.tonnagebandmax,
                lastupdated = EXCLUDED.lastupdated,
                factsheeturl = EXCLUDED.factsheeturl,
                substanceinformationpage = EXCLUDED.substanceinformationpage;
        ", conn)
        {
            Parameters =
            {
                new() { Value = id },
                new() { Value = substanceId },
                new() { Value = name },
                new() { Value = ecNumber },
                new() { Value = casNumber },
                new() { Value = registrationStatus },
                new() { Value = registrationType },
                new() { Value = submissionType },
                new() { Value = tonnageBand },
                new() { Value = tonnageBandMin },
                new() { Value = tonnageBandMax },
                new() { Value = lastUpdated },
                new() { Value = factsheetUrl },
                new() { Value = substanceInformationPage },
            }
        };

        cmd.ExecuteNonQuery();
    }

    private static void ExecuteNonQuery(NpgsqlConnection conn, string sql)
    {
        using var cmd = new NpgsqlCommand(sql, conn);
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        //NpgsqlConnection conn = new(TestConnectionString);
        //conn.Open();
        //NpgsqlCommand cmd = conn.CreateCommand();
        //cmd.CommandText = "truncate table USERS";
        //cmd.ExecuteNonQuery();

        //cmd.CommandText = "truncate table ORGANIZATIONS";
        //cmd.ExecuteNonQuery();

        //cmd.CommandText = "truncate table DATASHEETS";
        //cmd.ExecuteNonQuery();

        //cmd.CommandText = "truncate table DATASHEETFEEDITEMS";
        //cmd.ExecuteNonQuery();

        //cmd.CommandText = "truncate table DATASHEETFEEDS";
        //cmd.ExecuteNonQuery();

        //conn.Close();
        //conn.Dispose();
    }

    // public SqlConnection Db { get; private set; }
}

