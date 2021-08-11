using Azure.Core;
using Azure.Identity;
using DbUp;
using DbUp.Builder;
using DbUp.Engine;
using DbUp.Helpers;
using DbUp.Support;
using MsGraph = Microsoft.Graph;
using Newtonsoft.Json.Linq;
//using Npgsql;
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
    class Program
    {
        /// <summary>
        /// Example terminal command: dotnet AzureDbUp.dll --connection-string "Server=tcp:myserver.database.windows.net,1433;Initial Catalog=mydatabase"   
        /// </summary>
        /// <param name="dbEngine">Database engine. Options: SqlServer, MySql, PostgreSql</param>
        /// <param name="connectionString">Database connection string. Example: Server=tcp:myserver.database.windows.net,1433;Initial Catalog=mydatabase</param>
        /// <param name="authMode">Database connection authentication mode. Options: azure, sql</param>
        /// <param name="sqlFolder">Name of the folder with sql scripts. Example: sql</param>
        static async Task<int> Main(string dbEngine, string connectionString, string authMode, string sqlFolder = "sql")
        {
            //TODO:  Consider more user options? Feature flags?  Override logs, turn on or off?  
            
            // Print Azure DbUp welcome banner
            var font = FigletFont.Load("fonts/azdbup.flf");
            AnsiConsole.Render(new FigletText(font,"AZURE  DBUP").Color(Color.OrangeRed1));

            dbEngine = GetDbEngine(dbEngine);
            connectionString = GetConnectionString(connectionString);
            authMode = GetAuthMode(authMode);

            // Print conn settings table
            var connSettingsTable = GetConnSettingsTable(dbEngine,connectionString,authMode);
            AnsiConsole.Render(connSettingsTable);                      
            
            // Test database connection
            var connectErrorMessage = await TestConnect(dbEngine,connectionString, authMode);
            if (!String.IsNullOrEmpty(connectErrorMessage))
            {
                AnsiConsole.WriteLine($"{connectErrorMessage}");
                return -1;
            }
            else 
            {
                AnsiConsole.MarkupLine($"[green]Test connection successful![/]");
            }

            // Get directory info for sql subfolders
            var cwd = Directory.GetCurrentDirectory();
            //var sqlDir = Directory.GetDirectories(sqlFolder).FirstOrDefault();
            //AnsiConsole.MarkupLine($"Looking for .sql files in {cwd}{sqlDir}");
            var sqlFolderList = new List<string>();
            try
            {
                sqlFolderList = Directory.GetDirectories("sql").ToList(); //TODO: Make this foldername configurable? Add as a user command line input?
            }
            catch (System.Exception)
            {
                AnsiConsole.WriteLine($"The current working directory is {cwd}");
                throw;
            }
            var dirInfoList =  new List<DirectoryInfo>();
            sqlFolderList.ForEach(folder => {dirInfoList.Add(new DirectoryInfo(folder));});
            
            // Get DbUp run options and metadata for each folder 
            List<ScriptFolder> dbUpFolderList = GetDbUpFolders(dirInfoList);
            if (dbUpFolderList.Count <= 0)
            { 
                AnsiConsole.MarkupLine($"[red]Found no *.sql files in the subfolders of {cwd}\\sql[/]");
                AnsiConsole.MarkupLine($"[red]Exiting ... [/]");
                return -1;
            }

            var dbUpFolderSettingsTable = GetDbUpFolderSettingsTable(dbUpFolderList);
            AnsiConsole.Render(dbUpFolderSettingsTable);
                     
            // GO TIME! 
            // Execute sql scripts in each folder according to it's options.
            foreach (var scriptFolder in dbUpFolderList)
            {
                var runScriptsResult = RunScripts(scriptFolder, dbEngine, connectionString, authMode);
                if (!runScriptsResult.Successful)
                {
                    AnsiConsole.MarkupLine($"[red]{runScriptsResult.Error.Message}[/]"); 
                    return -1;
                }
            }
            AnsiConsole.MarkupLine("[blue]All set![/]");
            return 0;
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
                var selectionPrompt = new SelectionPrompt<string>().Title("[Blue]Select database engine[/]").AddChoices(dbEngineList);
                dbEngine = AnsiConsole.Prompt(selectionPrompt);
                AnsiConsole.MarkupLine($"Using db-engine: [Blue]{dbEngine}[/]");                
            }
            return dbEngine;
        }

        private static string GetConnectionString(string connectionString)
        {
            if (String.IsNullOrEmpty(connectionString))
            {            
                connectionString = AnsiConsole.Ask<string>("Please enter a connection string: ");
                AnsiConsole.MarkupLine($"Using connection-string: [Blue]{connectionString}[/]"); 
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
                var selectionPrompt = new SelectionPrompt<string>().Title("[blue]Select authentication mode[/]").AddChoices(authModeList);
                authMode = AnsiConsole.Prompt(selectionPrompt);
                AnsiConsole.MarkupLine($"Using auth-mode: [blue]{authMode}[/]");
            }
            return authMode;
        }

        private static async Task<string> TestConnect(string dbEngine, string connection, string authMode)
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
            AnsiConsole.WriteLine($"Testing connection to {dbEngine} ...");            
            var upgrader = upgradeEngineBuilder.WithScript("DbUp test connect","SELECT 1").Build();
            var connectErrorMessage = string.Empty;
            var connectResult = upgrader.TryConnect(out connectErrorMessage);
            return connectErrorMessage;
        }

        private static void ValidagteArguments(string connectionString, string connectionSecurity)
        {
            if (String.IsNullOrEmpty(connectionString))
            {
                AnsiConsole.MarkupLine("[red]Please set a connection string on the command line.[/]");
                AnsiConsole.MarkupLine("[red]Please set a connection string on the command line.[/]");
            }
        }
        private static List<ScriptFolder> GetDbUpFolders(List<DirectoryInfo> dirInfoList)
        {
            // Set the folder run settings 
            var scriptFolderList = new List<ScriptFolder>();
            // scriptFolderList.Add(new ScriptFolder{FolderPath = "prescripts", LogRun = false, RunAlways = true, SqlFileCount = 0});
            // scriptFolderList.Add(new ScriptFolder{FolderPath = "scripts", LogRun = true, RunAlways = false, SqlFileCount = 0});
            // scriptFolderList.Add(new ScriptFolder{FolderPath = "subscripts", LogRun = false, RunAlways = true, SqlFileCount = 0});

            // Get sql file count for each folder
            foreach (var dirInfo in dirInfoList)
            {
                var scriptFolder = new ScriptFolder();
                scriptFolder.FolderPath = dirInfo.FullName;
                scriptFolder.SqlFileCount = dirInfo.GetFiles("*.sql").Count();
                if (dirInfo.Name.Contains("always")) 
                {
                    scriptFolder.RunAlways = true;
                }
                if (dirInfo.Name.Contains("nolog"))
                {
                    scriptFolder.LogRun = false;
                }
                if (scriptFolder.SqlFileCount > 0)
                {
                    scriptFolderList.Add(scriptFolder);
                }
            }
            return scriptFolderList;
        }

        private static string MaskConnection(string connectionString) 
        {
            // Regex magic.  Mask the password.
            var maskedConnection = Regex.Replace(connectionString, @"(?<=(?<![^;])pass\w*=).*?(?=;[\w\s]+=|$)", "*****", RegexOptions.IgnoreCase);
            return maskedConnection;
        }

        private static Table GetConnSettingsTable(string dbEngine, string connectionString, string authMode)
        {
            var maskedConnection = MaskConnection(connectionString);
            var displayConnectionString = maskedConnection;

            var settingsDict = new Dictionary<string, string>() {
                { "db-engine", dbEngine},
                { "connection-string", displayConnectionString},
                { "auth-mode", authMode}
            };

            var settingsList = settingsDict.Where(x => !String.IsNullOrEmpty(x.Value)).ToList();
           
            var settingTable = new Table();
            settingTable.AddColumn(new TableColumn(new Markup("[OrangeRed1]Setting[/]")));
            settingTable.AddColumn(new TableColumn("[Blue]Value[/]"));
            
            settingsList.ForEach(setting => {settingTable.AddRow(setting.Key, setting.Value);});
            return settingTable;
        }

        private static Table GetDbUpFolderSettingsTable(List<ScriptFolder> dbUpFolderList)
        {
            var sqlFoldersTable = new Table();
            sqlFoldersTable.AddColumn(new TableColumn(new Markup("[OrangeRed1]Sql folder[/]")));
            sqlFoldersTable.AddColumn(new TableColumn("[Blue]File count [/]"));
            sqlFoldersTable.AddColumn(new TableColumn("[Blue]Always run[/]"));
            sqlFoldersTable.AddColumn(new TableColumn("[Blue]Log run[/]"));
            dbUpFolderList.ForEach(folder => {sqlFoldersTable.AddRow(folder.FolderPath,folder.SqlFileCount.ToString(),folder.RunAlways.ToString(),folder.LogRun.ToString());});
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
            AnsiConsole.MarkupLine($"Got azure token for identity [blue]{upn}[/]");
        
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
            
            if (!scriptFolder.LogRun)
            {
                upgradeEngineBuilder.JournalTo(new NullJournal());
            }

            var upgrader = upgradeEngineBuilder.Build();

            // Execute the scripts
            AnsiConsole.MarkupLine($"[blue]Running scripts in folder {scriptFolder.FolderPath}[/]");
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
            public bool LogRun { get; set; }
            public bool RunAlways { get; set; }
            public int SqlFileCount { get; set; }
        }
        
        public class DbEngine
        {
            public const string SqlServer = "sqlserver";
            public const string MySql = "mysql";
            public const string PostgresSql = "postgresql";
        }

        public class AuthMode
        {
            public const string AzureAuth = "azure";
            public const string SqlAuth = "sql";
        }
    }
}
