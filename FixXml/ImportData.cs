using Npgsql;
using System.Xml;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Azure.Identity;

namespace FixXml;

public class ImportData
{
    static string connString;

    private static readonly string SubstancesPath = ResolveInputPath(
        envVarName: "ESDSCOM_SUBSTANCES_XML_PATH",
        repoRelativePaths: new[]
        {
            Path.Combine("FixXml", "Data", "substances.xml")
        },
        legacyFallbackPath: @"c:\temp\substances.xml");

    private static readonly string PhrasesPath = ResolveInputPath(
        envVarName: "ESDSCOM_PHRASES_XML_PATH",
        repoRelativePaths: new[]
        {
            Path.Combine("FixXml", "Data", "phrases.xml"),
            // The repo already carries a phrases catalog used by the client.
            // Allow FixXml to use it by default to reduce setup friction.
            Path.Combine("Client", "Data", "Phrases.xml")
        },
        legacyFallbackPath: @"c:\temp\phrases.xml");

    public ImportData()
    {
        // Allow running the import utility without Azure Key Vault by supplying a connection string directly.
        // Useful for disposable Postgres environments (e.g., Neon) and offline development.
        string envConnString =
            Environment.GetEnvironmentVariable("ESDSCOM_CONNECTION_STRING") ??
            Environment.GetEnvironmentVariable("CONNECTION_STRING");

        if (!string.IsNullOrWhiteSpace(envConnString))
        {
            connString = envConnString;
        }

        if (string.IsNullOrEmpty(connString))
        {
            SecretClientOptions options = new()
            {
                Retry =
                    {
                        Delay= TimeSpan.FromSeconds(2),
                        MaxDelay = TimeSpan.FromSeconds(16),
                        MaxRetries = 5,
                        Mode = RetryMode.Exponential
                     }
            };
            try
            {
                var client = new SecretClient(new Uri("https://esdscomeditorkeyvault.vault.azure.net/"), new DefaultAzureCredential(), options);
                KeyVaultSecret secret = client.GetSecret("ConnectionString");
                connString = secret.Value;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Failed to resolve a database connection string for FixXml. " +
                    "Set ESDSCOM_CONNECTION_STRING (or CONNECTION_STRING) to a PostgreSQL connection string, " +
                    "or ensure Azure Key Vault access to https://esdscomeditorkeyvault.vault.azure.net/ with secret name 'ConnectionString'.",
                    ex);
            }
        }

        //  this is a 'one-time' utility to push source content for populating the db
        //  with content from the phrases and substances xml files that are obtained 
        //  from the ECHA and EUPHRAC sites
    }

    private static string ResolveInputPath(string envVarName, string[] repoRelativePaths, string legacyFallbackPath)
    {
        string envValue = Environment.GetEnvironmentVariable(envVarName);
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            string resolved = Path.IsPathRooted(envValue)
                ? envValue
                : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), envValue));

            if (!File.Exists(resolved))
            {
                throw new FileNotFoundException(
                    $"{envVarName} was set but the file was not found.",
                    resolved);
            }

            return resolved;
        }

        string repoRoot = FindRepoRoot();
        if (!string.IsNullOrWhiteSpace(repoRoot))
        {
            foreach (string repoRelativePath in repoRelativePaths)
            {
                string candidate = Path.Combine(repoRoot, repoRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        if (File.Exists(legacyFallbackPath))
        {
            return legacyFallbackPath;
        }

        string repoHints = string.Join("' or '", repoRelativePaths);
        throw new FileNotFoundException(
            $"Could not locate input XML. Set {envVarName} to an absolute path, or place the file at '{repoHints}' under the repo root, or (legacy) at '{legacyFallbackPath}'.");
    }

    private static int GetOptionalPositiveIntEnvVar(string envVarName)
    {
        string value = Environment.GetEnvironmentVariable(envVarName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (!int.TryParse(value, out int parsed) || parsed < 0)
        {
            throw new InvalidOperationException($"{envVarName} must be a non-negative integer (0 = unlimited). Value was '{value}'.");
        }

        return parsed;
    }

    private static string FindRepoRoot()
    {
        // Repo root in this workspace is expected to contain the solution file.
        // We walk up from the current working directory to support running FixXml from different locations.
        DirectoryInfo dir = new(Directory.GetCurrentDirectory());
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir.FullName, "eSDSCom.Editor.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return string.Empty;
    }

    public static void Run()
    {
        // Ensure connection string resolution (env var / Key Vault) runs.
        // Program.cs calls the static Run() without constructing ImportData.
        _ = new ImportData();

        ImportSubstances();

        ImportPhrases();
    }

    static void ImportSubstances()
    {
        try
        {
            var conn = new NpgsqlConnection(connString);

            var delCmd = new NpgsqlCommand(@"DELETE FROM SUBSTANCES", conn);
            conn.Open();
            delCmd.ExecuteNonQuery();
            conn.Close();

            XmlDocument xDoc = new();
            xDoc.Load(SubstancesPath);

            var nodes = xDoc.DocumentElement?.SelectNodes("results/result");
            if (nodes is null)
            {
                string rootName = xDoc.DocumentElement?.Name ?? "<null>";
                throw new InvalidOperationException(
                    $"Unexpected substances XML format. Expected root <RegisteredSubstances> containing <results><result>... nodes. Actual root was '{rootName}'. File: '{SubstancesPath}'.");
            }

            int maxResults = GetOptionalPositiveIntEnvVar("ESDSCOM_SUBSTANCES_MAX_RESULTS");

            // We want a single substance catalog row per ECHA substance <ID>.
            // The feed can contain multiple <result> rows for the same <ID> (e.g. FULL vs INTERMEDIATE,
            // or different dossiers / submission types). Our DB schema enforces uniqueness on substanceid,
            // so we collapse these variants and keep the most useful row.
            var selectedById = new Dictionary<string, SubstanceCandidate>(StringComparer.Ordinal);
            int scanned = 0;

            foreach (XmlNode node in nodes)
            {
                // For debugging, allow a quick run that targets N unique substances.
                // Note: this does not guarantee "newest" across the entire file unless you scan all nodes.
                if (maxResults > 0 && selectedById.Count >= maxResults)
                {
                    break;
                }

                scanned++;

                SubstanceCandidate candidate = SubstanceCandidate.FromXml(node);
                if (string.IsNullOrWhiteSpace(candidate.SubstanceId))
                {
                    continue;
                }

                if (!selectedById.TryGetValue(candidate.SubstanceId, out SubstanceCandidate existing))
                {
                    selectedById[candidate.SubstanceId] = candidate;
                    continue;
                }

                if (candidate.IsBetterThan(existing))
                {
                    selectedById[candidate.SubstanceId] = candidate;
                }
            }

            conn.Open();

            bool substancesNeedsId = TableHasColumn(conn, "substances", "id");

            foreach (SubstanceCandidate s in selectedById.Values.OrderBy(x => x.SubstanceId, StringComparer.Ordinal))
            {
                NpgsqlCommand insCmd;
                if (substancesNeedsId)
                {
                    insCmd = new NpgsqlCommand(@"INSERT INTO SUBSTANCES 
                                    (id, name,ecnumber,casnumber, substanceid,registrationstatus,registrationtype, 
                                     submissiontype,tonnageband,tonnagebandmin, tonnagebandmax,
                                     lastupdated, factsheetURL,substanceInformationPage)
                                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
                                    ON CONFLICT (substanceid) DO UPDATE SET
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
                                        factsheetURL = EXCLUDED.factsheetURL,
                                        substanceInformationPage = EXCLUDED.substanceInformationPage", conn)
                    {
                        Parameters =
                        {
                            new() { Value = DeterministicGuid("substance", s.SubstanceId) },
                            new() { Value = s.Name },
                            new() { Value = s.EcNumber },
                            new() { Value = s.CasNumber },
                            new() { Value = s.SubstanceId },
                            new() { Value = s.RegistrationStatus },
                            new() { Value = s.RegistrationType },
                            new() { Value = s.SubmissionType },
                            new() { Value = s.TonnageBand },
                            new() { Value = s.TonnageBandMin },
                            new() { Value = s.TonnageBandMax },
                            new() { Value = s.LastUpdated },
                            new() { Value = s.FactsheetUrl },
                            new() { Value = s.SubstanceInformationPage }
                        }
                    };
                }
                else
                {
                    insCmd = new NpgsqlCommand(@"INSERT INTO SUBSTANCES 
                                    (name,ecnumber,casnumber, substanceid,registrationstatus,registrationtype, 
                                     submissiontype,tonnageband,tonnagebandmin, tonnagebandmax,
                                     lastupdated, factsheetURL,substanceInformationPage)
                                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13)
                                    ON CONFLICT (substanceid) DO UPDATE SET
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
                                        factsheetURL = EXCLUDED.factsheetURL,
                                        substanceInformationPage = EXCLUDED.substanceInformationPage", conn)
                    {
                        Parameters =
                        {
                            new() { Value = s.Name },
                            new() { Value = s.EcNumber },
                            new() { Value = s.CasNumber },
                            new() { Value = s.SubstanceId },
                            new() { Value = s.RegistrationStatus },
                            new() { Value = s.RegistrationType },
                            new() { Value = s.SubmissionType },
                            new() { Value = s.TonnageBand },
                            new() { Value = s.TonnageBandMin },
                            new() { Value = s.TonnageBandMax },
                            new() { Value = s.LastUpdated },
                            new() { Value = s.FactsheetUrl },
                            new() { Value = s.SubstanceInformationPage }
                        }
                    };
                }

                insCmd.ExecuteNonQuery();
            }

            Console.WriteLine($"Substances import: scanned {scanned} feed rows, selected {selectedById.Count} unique substances.");
            conn.Close();
        }
        catch (Exception ex)
        {
            Console.Write(ex.Message);
            throw;
        }

    }

    static void ImportPhrases()
    {
        try
        {
            var conn = new NpgsqlConnection(connString);

            var delCmd = new NpgsqlCommand(@"DELETE FROM PHRASES", conn);
            conn.Open();
            delCmd.ExecuteNonQuery();
            conn.Close();

            XmlDocument xDoc = new();
            xDoc.Load(PhrasesPath);

            var nodes = xDoc.DocumentElement?.SelectNodes("PhraseItems/Phrase");
            if (nodes is null)
            {
                string rootName = xDoc.DocumentElement?.Name ?? "<null>";
                throw new InvalidOperationException(
                    $"Unexpected phrases XML format. Expected a root containing PhraseItems/Phrase nodes. Actual root was '{rootName}'. File: '{PhrasesPath}'.");
            }

            int maxResults = GetOptionalPositiveIntEnvVar("ESDSCOM_PHRASES_MAX_RESULTS");
            int processed = 0;

            conn.Open();

            bool phrasesNeedsId = TableHasColumn(conn, "phrases", "id");
            foreach (XmlNode node in nodes)
            {
                if (maxResults > 0 && processed >= maxResults)
                {
                    break;
                }

                NpgsqlCommand insCmd;
                if (phrasesNeedsId)
                {
                    insCmd = new NpgsqlCommand(@"INSERT INTO PHRASES 
                                    (id, StrucCode,XPath,Region,OrigCode,English,German,RevDate,Source,Info,Origin)
                                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
                                    ON CONFLICT (struccode) DO UPDATE SET
                                        xpath = EXCLUDED.xpath,
                                        region = EXCLUDED.region,
                                        origcode = EXCLUDED.origcode,
                                        english = EXCLUDED.english,
                                        german = EXCLUDED.german,
                                        revdate = EXCLUDED.revdate,
                                        source = EXCLUDED.source,
                                        info = EXCLUDED.info,
                                        origin = EXCLUDED.origin", conn)
                    {
                        Parameters =
                        {
                            new() { Value = Guid.NewGuid() },
                            new() { Value = GetValueFromNode(node,"StrucCode") },   //39
                            new() { Value = GetValueFromNode(node,"XPath") },       //218
                            new() { Value = GetValueFromNode(node,"Region") },      //2
                            new() { Value = GetValueFromNode(node,"OrigCode") },    //22
                            new() { Value = GetValueFromNode(node,"English") },     //405
                            new() { Value = GetValueFromNode(node,"German") },      //285
                            new() { Value = GetValueFromNode(node,"RevDate") },     //300
                            new() { Value = GetValueFromNode(node,"Source") },      //309
                            new() { Value = GetValueFromNode(node,"Info") },        //1167
                            new() { Value = GetValueFromNode(node,"Origin") }       //742
                        }
                    };
                }
                else
                {
                    insCmd = new NpgsqlCommand(@"INSERT INTO PHRASES 
                                    (StrucCode,XPath,Region,OrigCode,English,German,RevDate,Source,Info,Origin)
                                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
                                    ON CONFLICT (struccode) DO UPDATE SET
                                        xpath = EXCLUDED.xpath,
                                        region = EXCLUDED.region,
                                        origcode = EXCLUDED.origcode,
                                        english = EXCLUDED.english,
                                        german = EXCLUDED.german,
                                        revdate = EXCLUDED.revdate,
                                        source = EXCLUDED.source,
                                        info = EXCLUDED.info,
                                        origin = EXCLUDED.origin", conn)
                    {
                        Parameters =
                        {
                            new() { Value = GetValueFromNode(node,"StrucCode") },   //39
                            new() { Value = GetValueFromNode(node,"XPath") },       //218
                            new() { Value = GetValueFromNode(node,"Region") },      //2
                            new() { Value = GetValueFromNode(node,"OrigCode") },    //22
                            new() { Value = GetValueFromNode(node,"English") },     //405
                            new() { Value = GetValueFromNode(node,"German") },      //285
                            new() { Value = GetValueFromNode(node,"RevDate") },     //300
                            new() { Value = GetValueFromNode(node,"Source") },      //309
                            new() { Value = GetValueFromNode(node,"Info") },        //1167
                            new() { Value = GetValueFromNode(node,"Origin") }       //742
                        }
                    };
                }

                insCmd.ExecuteNonQuery();
                processed++;
            }
            conn.Close();
        }
        catch (Exception ex)
        {
            Console.Write(ex.Message);
            throw;
        }
    }

    static string GetValueFromNode(XmlNode node, string elemName)
    {
        if (node.SelectSingleNode(elemName) is null)
        {
            return string.Empty;
        }
        else
        {
            return node.SelectSingleNode(elemName).InnerText;
        }
    }

    private static bool TableHasColumn(NpgsqlConnection conn, string tableName, string columnName)
    {
        using var cmd = new NpgsqlCommand(
            @"SELECT 1
              FROM information_schema.columns
              WHERE table_schema = 'public'
                AND table_name = $1
                AND column_name = $2
              LIMIT 1", conn);

        cmd.Parameters.AddWithValue(tableName);
        cmd.Parameters.AddWithValue(columnName);

        object result = cmd.ExecuteScalar();
        return result is not null;
    }

    private static Guid DeterministicGuid(string scope, string key)
    {
        // Stable, content-derived GUID so repeated imports produce the same ids.
        // This avoids churn if other tables later reference these ids.
        // (Not a strict UUIDv5 implementation, but stable for our use case.)
        using var sha = SHA256.Create();
        byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(scope + ":" + key));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private readonly record struct SubstanceCandidate(
        string SubstanceId,
        string Name,
        string EcNumber,
        string CasNumber,
        string RegistrationStatus,
        string RegistrationType,
        string SubmissionType,
        string TonnageBand,
        string TonnageBandMin,
        string TonnageBandMax,
        string LastUpdated,
        string FactsheetUrl,
        string SubstanceInformationPage)
    {
        public static SubstanceCandidate FromXml(XmlNode node)
        {
            return new SubstanceCandidate(
                SubstanceId: GetValueFromNode(node, "ID"),
                Name: GetValueFromNode(node, "name"),
                EcNumber: GetValueFromNode(node, "ecNumber"),
                CasNumber: GetValueFromNode(node, "casNumber"),
                RegistrationStatus: GetValueFromNode(node, "registrationStatus"),
                RegistrationType: GetValueFromNode(node, "registrationType"),
                SubmissionType: GetValueFromNode(node, "submissionType"),
                TonnageBand: GetValueFromNode(node, "tonnageBand"),
                TonnageBandMin: GetValueFromNode(node, "tonnageBandMin"),
                TonnageBandMax: GetValueFromNode(node, "tonnageBandMax"),
                LastUpdated: GetValueFromNode(node, "lastUpdated"),
                FactsheetUrl: GetValueFromNode(node, "factsheetURL"),
                SubstanceInformationPage: GetValueFromNode(node, "substanceInformationPage"));
        }

        public bool IsBetterThan(SubstanceCandidate other)
        {
            // 1) Prefer a real CAS number (not empty / '-')
            bool thisHasCas = HasRealCas(CasNumber);
            bool otherHasCas = HasRealCas(other.CasNumber);
            if (thisHasCas != otherHasCas)
            {
                return thisHasCas;
            }

            // 2) Prefer newest lastUpdated (format is typically dd-MM-yyyy)
            DateTime thisDate = ParseLastUpdatedOrMin(LastUpdated);
            DateTime otherDate = ParseLastUpdatedOrMin(other.LastUpdated);
            int dateCompare = thisDate.CompareTo(otherDate);
            if (dateCompare != 0)
            {
                return dateCompare > 0;
            }

            // 3) Prefer Active
            bool thisActive = string.Equals(RegistrationStatus, "Active", StringComparison.OrdinalIgnoreCase);
            bool otherActive = string.Equals(other.RegistrationStatus, "Active", StringComparison.OrdinalIgnoreCase);
            if (thisActive != otherActive)
            {
                return thisActive;
            }

            // 4) Prefer FULL
            bool thisFull = string.Equals(RegistrationType, "FULL", StringComparison.OrdinalIgnoreCase);
            bool otherFull = string.Equals(other.RegistrationType, "FULL", StringComparison.OrdinalIgnoreCase);
            if (thisFull != otherFull)
            {
                return thisFull;
            }

            // Otherwise keep existing.
            return false;
        }

        private static bool HasRealCas(string cas)
        {
            if (string.IsNullOrWhiteSpace(cas))
            {
                return false;
            }

            return !string.Equals(cas.Trim(), "-", StringComparison.Ordinal);
        }

        private static DateTime ParseLastUpdatedOrMin(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return DateTime.MinValue;
            }

            // Examples in the ECHA feed: 21-12-2020
            if (DateTime.TryParseExact(value.Trim(), "dd-MM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsed))
            {
                return parsed;
            }

            return DateTime.MinValue;
        }
    }
}
