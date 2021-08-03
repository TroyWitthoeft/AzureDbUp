-- PRINT('DbUp tracks previously run scripts in the [SchemaVersions] table')
DROP TABLE IF EXISTS [AzureDbUpTest]
CREATE TABLE AzureDbUpTest (
    DbUpID int
);
