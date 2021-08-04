# AzureDbUp

![Azure DbUp Logo](./img/AzureDbUp-logo.png)

Update your database from anywhere. AzureDbUp is a dotnet console application that updates your target sql database from the commnand line.  You supply the `connection-string` and `sql-folder-path` and AzureDbUp will use DbUp to perform migration-based updates to your target database. 

> `dotnet AzureDbUp.dll --connection-string "Server=tcp:my-example-server.database.windows.net,1433;Initial Catalog=my-example-database"`

![Azure DbUp example](./img/AzureDbUp-Example-Run.gif)

## Contributing
Pull requests are welcome and encouraged! Help out! For major changes, please open an issue first to discuss what you would like to change.  If clone this repository to VS Code, remember to set your connection string inside the `launch.json` file.
 

### Project Dependencies

 - This application was developed in VS Code and targets the .NET 5.0 framework.
 - This application uses the [DbUp](https://dbup.readthedocs.io/) library to manage database updates. 
 - This application uses [Spectre Console](https://github.com/spectreconsole/spectre.console) to beautify the console output.
 - This application uses System CommandLine [DragonFruit](https://github.com/dotnet/command-line-api/wiki) library to simplify command line argument parsing.
 - This application uses the [Azure Identity](https://github.com/Azure/azure-sdk-for-net/blob/main/sdk/identity/Azure.Identity/README.md) library for authentication and azure sql token retrieval.
 - ~~This application uses [Microsoft Graph](https://docs.microsoft.com/en-us/graph/overview) library to walk and list the current users Azure AD security groups.~~



## License
[MIT](https://choosealicense.com/licenses/mit/)
