![Azure DbUp Logo](./img/AzureDbUp-logo.png)

# Overview

AzureDbUp is a lightweight, open-source, cross-platform, cross-database-engine application that runs sql migration scripts to keep your databases in sync and up to date.  AzureDbUp wraps the [DbUp](https://github.com/DbUp/DbUp) project in a console application that exposes the DbUp API as command line parameters.  This application was built to help [get your database changes under source control](https://blog.codinghorror.com/get-your-database-under-version-control/) where they can be rapidly reviewed, audited, and deployed. 

To get started, clone this repository to your CI/CD system of choice and have your developers commit sql change scripts files into the [sql folders](https://github.com/TroyWitthoeft/AzureDbUp/tree/main/sql).  Then, configure your pipeline to build this repository and execute the built application. When executed, AzureDbUp will make a test connection to your database, and then execute any new sql scripts that have been committed to the repository.  

 ![Azure DbUp demo](./img/AzureDbUp-demo.gif)

To automate the interactivity seen above, use command line parameters.

`dotnet AzureDbUp.dll --conn-string "Server=tcp:my-example-server.database.windows.net,1433;Initial Catalog=my-example-database" --db-engine "sqlserver" --auth-mode "azure"`

Through DbUp, AzureDbUp inherits support for multiple database engines such as **PostgreSQL**, **MySql**, along with **Sql Server**.  AzureDbUp also supports using **Azure Active Directory** tokens as part of your connection string to help keep database credentials out of your code repository and CI/CD system.   Please compare AzureDbUp to products like RedGate ReadyRoll, Liquibase, and Flyway.  


# Sql Folders

AzureDbUp tracks which new sql files to run using a DbUp feature called [journaling](https://dbup.readthedocs.io/en/latest/more-info/journaling/).  Sql files are executed in alphanumeric order, starting with foldername, and then files inside.  Out of the box this repository has three sql folders.  Sql files in the prescripts and subscripts folder are always ran and their runs are not logged to the DbUp `[SchemaVersions]` table.

![image](https://user-images.githubusercontent.com/1102958/147772108-a516e018-482f-4de2-b3e9-3e6daddbcf06.png)

The folder run behavior can be adjusted by adding/removing the `run-always` or `no-log` keywords from the folder name.  These keyword control the DbUp RunAlways and NullJournal behavior. RunAlways folders can be useful if you want to always apply certain scripts. Typically these are idempotent scripts that drop and create things like functions, views, procedures, and permissions. Or, database maintenance such as rebuilding indices, etc. 


## Try it locally
 - Prereq: Download and install [dotnet core](https://dotnet.microsoft.com/download).
 - Download the [latest release](https://github.com/TroyWitthoeft/AzureDbUp/releases/download/release-latest/release-latest.zip) of AzureDbUp. Unzip the files to a folder.
 - Edit and save your .sql scripts in the sql folder. 
 - Launch a terminal and call `dotnet AzureDbUp.dll`
 - Enter a connection string, choose your options, and let it rip!

## Databases supported

Azure DbUp currently supports: Azure Sql, MySql, PostgreSQL, CockoachDB 

![Azure DbUp databases](./img/AzureDbUp-databases.png)

Support for other databases is planned. 

## Contributing

This is a small project.  All feedback is welcome and every commit is a gift.  Pull requests are welcome and encouraged! 

## Project Dependencies

 - This application was developed in VS Code and targets the .NET 5.0 framework.
 - This application uses the [DbUp](https://dbup.readthedocs.io/) library to manage database updates. 
 - This application uses [Spectre Console](https://github.com/spectreconsole/spectre.console) to beautify the console output.
 - This application uses System CommandLine [DragonFruit](https://github.com/dotnet/command-line-api/wiki) library to simplify command line argument parsing.
 - This application uses the [Azure Identity](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/identity/Azure.Identity/README.md) library for authentication and azure sql token retrieval.
 - ~~This application uses [Microsoft Graph](https://docs.microsoft.com/en-us/graph/overview) library to walk and list the current users Azure AD security groups.~~


## License
[MIT](https://choosealicense.com/licenses/mit/)
