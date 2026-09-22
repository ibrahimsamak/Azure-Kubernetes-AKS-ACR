CREATE USER [id-orderflow-order] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-orderflow-order];
ALTER ROLE db_datawriter ADD MEMBER [id-orderflow-order];
ALTER ROLE db_ddladmin   ADD MEMBER [id-orderflow-order];   -- only because EF migrations run at startup

CREATE USER [id-orderflow-inventory] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-orderflow-inventory];
ALTER ROLE db_datawriter ADD MEMBER [id-orderflow-inventory];
ALTER ROLE db_ddladmin   ADD MEMBER [id-orderflow-inventory];

CREATE USER [id-orderflow-payment] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-orderflow-payment];
ALTER ROLE db_datawriter ADD MEMBER [id-orderflow-payment];
ALTER ROLE db_ddladmin   ADD MEMBER [id-orderflow-payment];

CREATE USER [id-orderflow-notification] FROM EXTERNAL PROVIDER;
ALTER ROLE db_datareader ADD MEMBER [id-orderflow-notification];
ALTER ROLE db_datawriter ADD MEMBER [id-orderflow-notification];
ALTER ROLE db_ddladmin   ADD MEMBER [id-orderflow-notification];

SELECT name, type_desc, authentication_type_desc FROM sys.database_principals WHERE name LIKE 'id-orderflow-%';
