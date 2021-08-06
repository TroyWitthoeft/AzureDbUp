-- PRINT('DbUp tracks previously run scripts in the SchemaVersions table')
SELECT * FROM schemaversions
FETCH FIRST 3 ROWS ONLY