using Azure.Core;
using Azure.Identity;
using DbUp;
using DbUp.Engine;
using DbUp.Helpers;
using DbUp.Support;
using MsGraph = Microsoft.Graph;
using Newtonsoft.Json.Linq;
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

namespace AzureDbUp
{
    class Program
    {
        /// <summary>
        /// Example terminal command: dotnet AzureDbUp.dll --connection-string "Server=tcp:myserver.database.windows.net,1433;Initial Catalog=mydatabase" --connection-security azure --pre-scripts-path PreScripts/ --scripts-path Scripts/ --sub-scripts-path SubScripts/  
        /// </summary>
        /// <param name="connectionString">Database connection string. Example: Server=tcp:myserver.database.windows.net,1433;Initial Catalog=mydatabase</param>
        /// <param name="connectionSecurity">Database security. Options include: azure, sql</param>
        static async Task<int> Main(string connectionString = "localhost", string connectionSecurity = "" )
        {
            // Print Azure DbUp welcome banner
            var font = FigletFont.Load("fonts/azdbup.flf");
            AnsiConsole.Render(new FigletText(font,"AZURE  DBUP").Color(Color.OrangeRed1));

            // Get directory info for sql subfolders
            var cwd = Directory.GetCurrentDirectory();
            var sqlFolderList = new List<string>();
            try
            {
                sqlFolderList = Directory.GetDirectories("sql").ToList(); //TODO: Make this foldername configurable? Add to command line?
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

            // Check connection string and set connection security
            SqlConnectionStringBuilder sqlConnection = parseConnection(connectionString);
            if (sqlConnection.UserID != String.Empty && sqlConnection.Password != String.Empty) 
            {
                AnsiConsole.WriteLine($"The connection string has a username and password set.  Switching to sql account security.");
                connectionSecurity = "sql";
            }
            else
            {
                connectionSecurity = "azure";
            }
            
            // Print settings
            string maskedConnection = maskConnection(sqlConnection);
            var connSettingsTable = GetConnSettingsTable(maskedConnection,connectionSecurity);
            AnsiConsole.Render(connSettingsTable);
            var dbUpFolderSettingsTable = GetDbUpFolderSettingsTable(dbUpFolderList);
            AnsiConsole.Render(dbUpFolderSettingsTable);
         
            // Check azure ad connection
            if (String.Equals(connectionSecurity, "azure")) 
            {
                AnsiConsole.WriteLine($"Testing connection to azure security ...");
                var credential = new DefaultAzureCredential();
                var token = await GetToken(credential);
                var name = GetNameFromToken(token);
                //var securityGroups = await GetAzureSecurityGroups(credential);  //TODO: 
            }
            
            // Go time! Execute sql scripts in each folder according to it's options.
            foreach (var scriptFolder in dbUpFolderList)
            {
                var runScriptsResult = RunScripts(scriptFolder,connectionString,connectionSecurity);
                if (!runScriptsResult.Successful)
                {
                    AnsiConsole.MarkupLine($"[red]{runScriptsResult.Error.Message}[/]"); 
                    return -1;
                }
            }
            AnsiConsole.MarkupLine("[blue]All set![/]");
            return 0;
        }

        private record ScriptFolder
        {
            public string FolderPath { get; set;}
            public bool LogRun { get; init; }
            public bool RunAlways { get; init; }
            public int SqlFileCount { get; set; }
        }
        private static List<ScriptFolder> GetDbUpFolders(List<DirectoryInfo> dirInfoList)
        {
            // Set the folder run settings 
            var scriptFolderList = new List<ScriptFolder>();
            scriptFolderList.Add(new ScriptFolder{FolderPath = "prescripts", LogRun = false, RunAlways = true, SqlFileCount = 0});
            scriptFolderList.Add(new ScriptFolder{FolderPath = "scripts", LogRun = true, RunAlways = false, SqlFileCount = 0});
            scriptFolderList.Add(new ScriptFolder{FolderPath = "subscripts", LogRun = false, RunAlways = true, SqlFileCount = 0});

            // Get sql file count for each folder
            foreach (var dirInfo in dirInfoList)
            {
                int sqlFileCount = 0;
                if (scriptFolderList.Any(x => x.FolderPath.Contains(dirInfo.Name)))
                {
                    sqlFileCount = dirInfo.GetFiles("*.sql").Count();
                    var scriptFolder = scriptFolderList.FirstOrDefault(x => x.FolderPath == dirInfo.Name);
                    scriptFolder.FolderPath = dirInfo.FullName;
                    scriptFolder.SqlFileCount = sqlFileCount;
                }
            }
            scriptFolderList.RemoveAll(x => x.SqlFileCount == 0);
            return scriptFolderList;
        }

        private static SqlConnectionStringBuilder parseConnection(string connectionString)
        {
            var connection = new SqlConnectionStringBuilder();
            try
            {
                connection = new SqlConnectionStringBuilder(connectionString);
            }
            catch (System.Exception ex)
            { 
                AnsiConsole.MarkupLine($"[red]We were unable to parse the following connection string.[/]"); 
                AnsiConsole.MarkupLine($"[red]{connectionString}[/]");
                AnsiConsole.MarkupLine($"[red]Is the connection string argument formatted correctly?[/]"); 
                AnsiConsole.WriteException(ex); 
                throw;
            }
            return connection;
        }
        private static string maskConnection(SqlConnectionStringBuilder connection)
        {
            if (connection.ContainsKey("password") && connection.Password != String.Empty) 
            {
                connection["password"] = "*****";
            }
            return connection.ToString();
        }

        private static Table GetConnSettingsTable(string connectionString, string connectionSecurity)
        {
            var settingsDict = new Dictionary<string, string>() {
                { "connection string", connectionString},
                { "connection secruity", connectionSecurity}
            };

            var settingsList = settingsDict.ToList();
           
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
            sqlFoldersTable.AddColumn(new TableColumn("[Blue]Log run[/]"));
            sqlFoldersTable.AddColumn(new TableColumn("[Blue]Always run[/]"));
            sqlFoldersTable.AddColumn(new TableColumn("[Blue]File count[/]"));
            dbUpFolderList.ForEach(folder => {sqlFoldersTable.AddRow(folder.FolderPath,folder.LogRun.ToString(),folder.RunAlways.ToString(),folder.SqlFileCount.ToString());});
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
            AnsiConsole.WriteLine($"Got azure token for identity {upn}");
        
            // // Serialize and pretty print full json token to log. 
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

        private static DatabaseUpgradeResult RunScripts(ScriptFolder scriptFolder, string connectionString, string connectionSecurity)
        {

            SqlConnectionStringBuilder sqlConnection = parseConnection(connectionString);
            
            // Set DbUp run options
            var sqlScriptOptions = new SqlScriptOptions();   
            if (scriptFolder.RunAlways)
            {
                sqlScriptOptions.ScriptType = ScriptType.RunAlways;
            }
            bool useAzureSqlIntegratedSecurity = false;
            if (String.Equals(connectionSecurity, "azure"))
            {
                useAzureSqlIntegratedSecurity = true;
            }
            var upgradeEngineBuilder = 
                DeployChanges.To
                    .SqlDatabase(connectionString,"dbo",useAzureSqlIntegratedSecurity)
                    .WithScriptsFromFileSystem(scriptFolder.FolderPath, sqlScriptOptions)
                    .LogToConsole()
                    .LogScriptOutput();
            
            if (!scriptFolder.LogRun)
            {
                upgradeEngineBuilder.JournalTo(new NullJournal());
            }
            var upgrader = upgradeEngineBuilder.Build();
            
            // Test connect
            var connectMessage = "";
            AnsiConsole.WriteLine($"Attempting connection to {sqlConnection.InitialCatalog}");
            var connectResult = upgrader.TryConnect(out connectMessage);  //TODO: Add retries for Azure SQL Serverless cold start? 
            if (!connectResult)
            {
                AnsiConsole.WriteLine($"Connect error message: {connectMessage}");
                Environment.Exit(-1);
            }
            AnsiConsole.WriteLine($"Connection successful!");

            // Execute the scripts
            AnsiConsole.MarkupLine($"[blue]Running scripts in folder {scriptFolder.FolderPath}[/]");
            var result = upgrader.PerformUpgrade();
            return result;
        }
    }
}
