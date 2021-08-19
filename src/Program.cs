using Azure.Core;
using Azure.Identity;
using DbUp;
using DbUp.Builder;
using DbUp.Engine;
using DbUp.Helpers;
using DbUp.Support;
using MsGraph = Microsoft.Graph;
using Newtonsoft.Json.Linq;
using Npgsql;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.CommandLine.DragonFruit;
using Microsoft.Data.SqlClient;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Data.Common;
using System.Reflection;
using System.Collections;

namespace AzureDbUp
{
    public class Program
    {
        /// <summary>
        /// AzureDbUp is a dotnet console application that updates your target sql database from the commnand line using DbUp.  
        /// DbUp tracks which SQL scripts have been run already, and runs the change scripts that are needed to get your database up to date.
        ///
        /// Example terminal command: dotnet AzureDbUp.dll
        /// Example terminal command with parameters: dotnet AzureDbUp.dll 
        /// </summary>
        /// <param name="dbEngine">Required. Database engine. Options: sqlserver, mysql, postgresql</param>
        /// <param name="connString">Required. Database connection string. Example: Server=tcp:myserver.database.windows.net,1433;Initial Catalog=mydatabase</param>
        /// <param name="authMode">Required. Database connection authentication mode. Options: azure, sql</param>
        /// <param name="sqlFolder">Optional. Relative or absolute path to folder with sql scripts. Defaults to sql. Example: sql, C:\AzureDbUp\sql </param>
        async Task<int> Main(string dbEngine, string connString, string authMode, string sqlFolder = "sql")
        {
            //TODO:  Consider more user options for controlling DbUp behavior? Feature flags?  Silence dbup logs? What about Structured ILogger?
            
            // Print AzureDbUp welcome banner
            LogBanner();

            // Get database connection settings
            connString = GetConnectionString(connString);
            dbEngine = GetDbEngine(dbEngine);            
            authMode = GetAuthMode(authMode);

            // Print conn settings table
            var connSettingsTable = GetConnSettingsTable(dbEngine,connString,authMode);
            AnsiConsole.Render(connSettingsTable);

            // Test database connection
            var connectErrorMessage = await TestConnect(dbEngine,connString, authMode);
            if (!String.IsNullOrEmpty(connectErrorMessage))
            {
                AnsiConsole.WriteLine($"{connectErrorMessage}");
                return -1;
            }
            else
            {
                AnsiConsole.MarkupLine($"[lime]Test connection successful![/]");
                AnsiConsole.MarkupLine($"");
            }

            // Get folder list and folder settings
            var listScriptFolder = GetScriptFolders(sqlFolder);
            if (listScriptFolder.Count == 0)
            {
                AnsiConsole.MarkupLine($"[red]Could could not find any .sql files in the the subfolders of {new DirectoryInfo(sqlFolder).FullName}[/]");
                AnsiConsole.MarkupLine($"[red]Exiting ...[/]");
                return -1;
            }
            else 
            {
                AnsiConsole.MarkupLine($"[lime]Found {listScriptFolder.Count} folders with .sql files![/]");
                AnsiConsole.MarkupLine($"");
            }

            // Print folder settings
            var folderSettingsTable = GetFolderSettings(listScriptFolder);            
            AnsiConsole.Render(folderSettingsTable);
            
            AnsiConsole.MarkupLine($"[green]Let's run scripts![/]");
            AnsiConsole.MarkupLine($"");
            int successCount = 0;
            foreach (var scriptFolder in listScriptFolder)
            {
                var runScriptsResult = RunScripts(scriptFolder, dbEngine, connString, authMode);
                successCount += runScriptsResult.Scripts.Count();
                if (!runScriptsResult.Successful)
                {
                    AnsiConsole.MarkupLine($"[red]{runScriptsResult.Error.Message}[/]");
                    return -1;
                }
            }

            AnsiConsole.MarkupLine($"[lime]All set! Executed {successCount} scripts successfully![/]");
            return 0;
        }

        private static List<ScriptFolder> GetScriptFolders(string sqlFolder)
        {
            var sqlFolderInfo = new DirectoryInfo(sqlFolder);
            AnsiConsole.MarkupLine($"Checking folder [blue]{sqlFolderInfo.FullName}[/] for subfolders with .sql files ...");
            List<ScriptFolder> listScriptFolder = new List<ScriptFolder>();
            
            if (!sqlFolderInfo.Exists)
            {
                AnsiConsole.MarkupLine($"[Red]Did not find folder {sqlFolderInfo.FullName}[/]");
                throw new System.IO.DirectoryNotFoundException();
            }

            var infoList = sqlFolderInfo.GetDirectories();

            foreach (var info in infoList)
            {
                var scriptFolder = new ScriptFolder();
                scriptFolder.FolderPath = info.FullName;
                scriptFolder.SqlFileCount = info.GetFiles("*.sql").Count(); //TODO: Consider run-always and no-log at file level?
                if (scriptFolder.SqlFileCount > 0)
                {
                    if (info.Name.Contains("run-always"))
                    {
                        scriptFolder.RunAlways = true;
                    }
                    if (info.Name.Contains("no-log"))
                    {
                        scriptFolder.NoLog = true;
                    }
                    listScriptFolder.Add(scriptFolder);
                }
            }
            return listScriptFolder;
        }

        private static string GetDbEngine(string dbEngine)
        {
            var dbEngineList = typeof(DbEngine).GetFields().Select(x => x.GetValue(null).ToString()).ToList();  //Using reflection to get string constant values from class.
            var listHasEngine = dbEngineList.Contains(dbEngine);
            if (!String.IsNullOrEmpty(dbEngine) && !listHasEngine)
            {
                AnsiConsole.MarkupLine($"AzureDbUp doesn't support engine [Blue]{dbEngine}[/] yet.  Pick one from a list ...");
            }
            if (listHasEngine == false)
            {
                var selectionPrompt = new SelectionPrompt<string>().Title("[orangered1]Select db-engine[/]:").AddChoices(dbEngineList);
                dbEngine = AnsiConsole.Prompt(selectionPrompt);
                AnsiConsole.MarkupLine($"[orangered1]Select db-engine[/]: [Blue]{dbEngine}[/]");
            }
            return dbEngine;
        }

        private static string GetConnectionString(string connectionString)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                connectionString = AnsiConsole.Ask<string>("[orangered1]Type conn-string[/]");
                AnsiConsole.MarkupLine($"                  [Blue]{connectionString}[/]");
            }
            return connectionString;
        }

        private static string GetAuthMode(string authMode)
        {
            var authModeList = typeof(AuthMode).GetFields().Select(x => x.GetValue(null).ToString()).ToList();
            var listHasAuthMode = authModeList.Contains(authMode);
            if (!String.IsNullOrEmpty(authMode) && !listHasAuthMode)
            {
                AnsiConsole.MarkupLine($"AzureDbUp doesn't support auth mode [Blue]{authMode}[/].  Pick one from a list ...");
            }
            if (listHasAuthMode == false)
            {
                var selectionPrompt = new SelectionPrompt<string>().Title("[orangered1]Select auth-mode[/]:").AddChoices(authModeList);
                authMode = AnsiConsole.Prompt(selectionPrompt);
                AnsiConsole.MarkupLine($"[orangered1]Select auth-mode[/]: [blue]{authMode}[/]");
            }
            return authMode;
        }

        private async Task<string> TestConnect(string dbEngine, string connection, string authMode)
        {
            // Check azure ad connection
            if (authMode.Equals(AuthMode.AzureAuth,StringComparison.InvariantCultureIgnoreCase))
            {
                AnsiConsole.WriteLine($"Testing connection to azure security ...");
                var credential = new DefaultAzureCredential();
                var token = await GetToken(credential);
                var name = GetNameFromToken(token);
                //var securityGroups = await GetAzureSecurityGroups(credential);  // TODO: Should we list AAD groups?  Future Feature flag?
            }

            var upgradeEngineBuilder = GetUpgradeEngineBuilder(dbEngine, connection, authMode);
            AnsiConsole.MarkupLine($"Testing connection to [blue]{MaskConnection(connection)}[/] ...");  
            var upgrader = upgradeEngineBuilder.WithScript("DbUp test connect","SELECT 1").Build();
            var connectErrorMessage = string.Empty;
            var connectResult = upgrader.TryConnect(out connectErrorMessage);
            return connectErrorMessage;
        }

        public string MaskConnection(string connectionString)
        {
            // Regex magic.  Mask the password.
            var maskedConnection = Regex.Replace(connectionString, @"(?<=(?<![^;])pass\w*=).*?(?=;[\w\s]+=|$)", "*****", RegexOptions.IgnoreCase);
            return maskedConnection;
        }

        private Table GetConnSettingsTable(string dbEngine, string connectionString, string authMode)
        {
            var maskedConnection = MaskConnection(connectionString);
            var displayConnectionString = maskedConnection;

            var settingsDict = new Dictionary<string, string>() {
                { "conn-string", displayConnectionString},
                { "db-engine", dbEngine},
                { "auth-mode", authMode}
            };

            var settingsList = settingsDict.Where(x => !String.IsNullOrEmpty(x.Value)).ToList();

            var settingTable = new Table();
            settingTable.AddColumn(new TableColumn(new Markup("[OrangeRed1]setting      [/]")));
            settingTable.AddColumn(new TableColumn("[OrangeRed1]value[/]"));

            settingsList.ForEach(setting => {settingTable.AddRow(new Markup(setting.Key), new Markup($"[blue]{setting.Value}[/]"));});
            return settingTable;
        }

        private static Table GetFolderSettings(List<ScriptFolder> dbUpFolderList)
        {
            var sqlFoldersTable = new Table();
            sqlFoldersTable.AddColumn(new TableColumn(new Markup("[OrangeRed1]folder[/]")));
            sqlFoldersTable.AddColumn(new TableColumn("[OrangeRed1].sql files[/]"));
            sqlFoldersTable.AddColumn(new TableColumn("[OrangeRed1]run-always[/]"));
            sqlFoldersTable.AddColumn(new TableColumn("[OrangeRed1]no-log[/]"));
            dbUpFolderList.ForEach(folder => {sqlFoldersTable.AddRow(folder.FolderPath,folder.SqlFileCount.ToString(),folder.RunAlways.ToString(),folder.NoLog.ToString());});
            return sqlFoldersTable;
        }
        private static async Task<string> GetToken(DefaultAzureCredential credential)
        {
            try
            {
                // Get an azure SQL access token
                var tokenRequestContext = new TokenRequestContext(new[] { "https://database.windows.net//.default" });
                //string scopes = String.Join(", ", tokenRequestContext.Scopes);
                AnsiConsole.WriteLine($"Getting an azure sql token ...");
                var tokenRequestResult = await credential.GetTokenAsync(tokenRequestContext);
                var token = tokenRequestResult.Token;
                return token;
            }
            catch (Exception ex)
            {
                AnsiConsole.WriteException(ex);
                throw;
            }
        }
        private static string GetNameFromToken(string token)
        {
            // Who are we? Get the name value from the json token's payload claims.
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;
            var upn = jsonToken.Claims.First(x => x.Type == "upn").Value;
            AnsiConsole.MarkupLine($"Got azure token for [blue]{upn}[/]");

            // // TODO: Make this an option? Serialize and pretty print full json token to log?  Feature flag?
            // var options = new JsonSerializerOptions { WriteIndented = true};
            // var payload = jsonToken.Payload.SerializeToJson();;
            // var json = JToken.Parse(payload).ToString();
            // AnsiConsole.WriteLine($"{json}");

            return upn;
        }
        private static async Task<List<string>> GetAzureSecurityGroups(DefaultAzureCredential credential)
        {
            // Get azure ad token
            AnsiConsole.WriteLine($"Getting an azure graph token ...");
            var graphToken = credential.GetToken(new TokenRequestContext(new[] { "https://graph.microsoft.com/.default" }));
            var graphAccessToken = graphToken.Token;

            // Get my azure ad security groups
            AnsiConsole.WriteLine($"Getting azure ad groups ...");
            var graphServiceClient = new MsGraph.GraphServiceClient(
                new MsGraph.DelegateAuthenticationProvider((requestMessage) =>
                {
                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", graphAccessToken);
                    return Task.CompletedTask;
                }));

            var result = await graphServiceClient.Me.MemberOf.Request().GetAsync();

            var azureSecurityGroupList = new List<String>();
            foreach (var directoryObject in result)
            {
                if (directoryObject is MsGraph.Group)
                {
                    var group = (MsGraph.Group)directoryObject;
                    var displayName = group.DisplayName;
                    azureSecurityGroupList.Add(displayName);
                }
            }
            return azureSecurityGroupList;
        }

        private static DatabaseUpgradeResult RunScripts(ScriptFolder scriptFolder, string dbEngine, string connectionString, string authMode)
        {

            // Set DbUp run options
            var sqlScriptOptions = new SqlScriptOptions();
            var upgradeEngineBuilder = GetUpgradeEngineBuilder(dbEngine,connectionString,authMode);
            if (scriptFolder.RunAlways)
            {
                sqlScriptOptions.ScriptType = ScriptType.RunAlways;
            }
            upgradeEngineBuilder.WithScriptsFromFileSystem(scriptFolder.FolderPath, sqlScriptOptions).LogToConsole().LogScriptOutput();

            if (scriptFolder.NoLog)
            {
                upgradeEngineBuilder.JournalTo(new NullJournal());
            }

            var upgrader = upgradeEngineBuilder.Build();

            // Execute the scripts
            AnsiConsole.MarkupLine($"Running scripts in [blue]{scriptFolder.FolderPath}[/]");
            var result = upgrader.PerformUpgrade();
            return result;
        }

        private static UpgradeEngineBuilder GetUpgradeEngineBuilder(string dbEngine, string connectionString, string authMode)
        {
            var upgradeEngineBuilder = new UpgradeEngineBuilder();
            // TODO: Create IDbEngine interface and have implementations of each specific db engine?
            if (dbEngine == DbEngine.PostgresSql)
            {
                upgradeEngineBuilder = DeployChanges.To.PostgresqlDatabase(connectionString);
            }

            if (dbEngine == DbEngine.MySql)
            {
                upgradeEngineBuilder = DeployChanges.To.MySqlDatabase(connectionString);
            }

            if (dbEngine == DbEngine.SqlServer)
            {
                var useAzureSqlIntegratedSecurity = false;
                useAzureSqlIntegratedSecurity = authMode == AuthMode.AzureAuth ? true : false;
                upgradeEngineBuilder = DeployChanges.To.SqlDatabase(connectionString,null,useAzureSqlIntegratedSecurity);
            }
            return upgradeEngineBuilder;
        }

        private record ScriptFolder
        {
            public string FolderPath { get; set;}
            public bool NoLog { get; set; }
            public bool RunAlways { get; set; }
            public int SqlFileCount { get; set; }
        }

        public class DbEngine
        {
            public const string MySql = "mysql";
            public const string SqlServer = "sqlserver";
            public const string PostgresSql = "postgresql";
        }

        public class AuthMode
        {
            public const string SqlAuth = "sql";
            public const string AzureAuth = "azure";
        }

        private static void LogBanner()
        {
           AnsiConsole.MarkupLine($@"");   
           AnsiConsole.MarkupLine($@"    [grey70]     ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[/]      ");   
           AnsiConsole.MarkupLine($@"    [grey70]  ▓▓▓[/][chartreuse3]▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[/][grey70]▓▓▓[/]   ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|[/][white]▓▓▓▓[/][chartreuse3]▓▓[/][lime]▓▓▓▓▓▓▓▓▓▓▓▓▓▓[/][chartreuse3]▓▓[/][white]▓▓▓▓[/][dodgerblue3]|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓[/][white]▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓[/][dodgerblue2]▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓▓▓▓▓▓▓[/][orangered1]/▓▓\[/][dodgerblue2]▓▓▓▓▓▓▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓▓▓▓▓▓[/][orangered1]/▓▓▓▓\[/][dodgerblue2]▓▓▓▓▓▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓▓▓▓▓[/][orangered1]/▓▓▓▓▓▓\[/][dodgerblue2]▓▓▓▓▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓▓▓▓[/][orangered1]/▓▓▓▓▓▓▓▓\[/][dodgerblue2]▓▓▓▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓▓▓[/][orangered1]/▓▓▓▓/\▓▓▓▓\[/][dodgerblue2]▓▓▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓▓[/][orangered1]/▓▓▓▓/[/][dodgerblue3]▓[/][dodgerblue2]▓[/][orangered1]\▓▓▓▓\[/][dodgerblue2]▓▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓▓[/][orangered1]/▓▓▓▓/[/][dodgerblue3]▓▓[/][dodgerblue2]▓▓[/][orangered1]\▓▓▓▓\[/][dodgerblue2]▓▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓▓[/][orangered1]/▓▓▓▓/[/][dodgerblue3]▓▓▓[/][dodgerblue2]▓▓▓[/][orangered1]\▓▓▓▓\[/][dodgerblue2]▓▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓▓[/][orangered1]/▓▓▓▓/[/][dodgerblue3]▓▓▓▓[/][dodgerblue2]▓▓▓▓[/][orangered1]\▓▓▓▓\[/][dodgerblue2]▓▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"    [dodgerblue3]|▓▓[/][orangered1]/▓▓▓▓/[/][dodgerblue3]▓▓▓▓▓[/][dodgerblue2]▓▓▓▓▓[/][orangered1]\▓▓▓▓\[/][dodgerblue2]▓▓|[/] ");   
           AnsiConsole.MarkupLine($@"       [dodgerblue3]▓▓▓▓▓▓▓▓▓▓▓[/][dodgerblue2]▓▓▓▓▓▓▓▓▓▓▓[/]     ");   
           AnsiConsole.MarkupLine($@"");   
           AnsiConsole.MarkupLine($@"Welcome to [bold]AzureDbUp[/]!  Let's get started!");
           AnsiConsole.MarkupLine($@"");                                                                                                             
        }

    }
}
