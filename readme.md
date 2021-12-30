![Azure DbUp Logo](./img/AzureDbUp-logo.png)

AzureDbUp is a lightweight, open-source, cross-platform, cross-database-engine application that runs sql migration scripts to keep your databases in sync and up to date.  Please compare AzureDbUp to products like RedGate ReadyRoll, Liquibase, and Flyway.  AzureDbUp wraps the [DbUp](https://github.com/DbUp/DbUp) project in a console application that can be run as part of a continous deployment devops pipeline.  

 ![Azure DbUp demo](./img/AzureDbUp-demo.gif)

Above, the compiled application is running locally in interactive mode.  Answering the interactive questions ahead of time as commandline parameters allows AzureDbUp to be run unattended, as part of a deployment pipeline!

`dotnet AzureDbUp.dll --conn-string "Server=tcp:my-example-server.database.windows.net,1433;Initial Catalog=my-example-database" --db-engine "sqlserver" --auth-mode "azure"`


Through DbUp, AzureDbUp inherits support for multiple database engines such as **PostgreSQL**, **MySql**, along with **Sql Server**.  AzureDbUp also supports using **azure active directory** tokens as part of your connection string to help keep database credentials out of your code repository.  

## Getting Started 
 - Prereq: Download and install [dotnet core](https://dotnet.microsoft.com/download).
 - Download the [latest release](https://github.com/TroyWitthoeft/AzureDbUp/releases/download/release-latest/release-latest.zip) of AzureDbUp. Unzip the files to a folder.
 - Edit and save your .sql scripts in the sql folder. 
 - Launch a terminal and call `dotnet AzureDbUp.dll`
 - Enter a connection string, choose your options, and let it rip!

## Databases supported

Azure DbUp currently supports: Azure Sql, MySql, PostgreSQL, CockoachDB 

![Azure DbUp databases](./img/AzureDbUp-databases.png)

More support for other databases is planned. 
## Contributing

This is a small project and we encourage support!  Have a feedback, please share it?  
Have a feature idea? Found a bug?  Pull requests are welcome and encouraged! 

## Project Dependencies

 - This application was developed in VS Code and targets the .NET 5.0 framework.
 - This application uses the [DbUp](https://dbup.readthedocs.io/) library to manage database updates. 
 - This application uses [Spectre Console](https://github.com/spectreconsole/spectre.console) to beautify the console output.
 - This application uses System CommandLine [DragonFruit](https://github.com/dotnet/command-line-api/wiki) library to simplify command line argument parsing.
 - This application uses the [Azure Identity](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/identity/Azure.Identity/README.md) library for authentication and azure sql token retrieval.
 - ~~This application uses [Microsoft Graph](https://docs.microsoft.com/en-us/graph/overview) library to walk and list the current users Azure AD security groups.~~


## License
[MIT](https://choosealicense.com/licenses/mit/)
