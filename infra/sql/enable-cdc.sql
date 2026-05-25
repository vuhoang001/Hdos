-- Bước 1: Enable SQL Server Agent (chạy 1 lần trong container)
-- docker exec -it hdos-sqlserver /opt/mssql/bin/mssql-conf set sqlagent.enabled true
-- docker restart hdos-sqlserver

-- Bước 2: Enable CDC trên database OrderDb
USE OrderDb;
GO

IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = 'OrderDb' AND is_cdc_enabled = 1)
BEGIN
    EXEC sys.sp_cdc_enable_db;
    PRINT 'CDC enabled on OrderDb';
END
ELSE
    PRINT 'CDC already enabled on OrderDb';
GO

-- Bước 3: Enable CDC trên bảng Orders
IF NOT EXISTS (
    SELECT 1 FROM cdc.change_tables
    WHERE source_object_id = OBJECT_ID('dbo.Orders')
)
BEGIN
    EXEC sys.sp_cdc_enable_table
        @source_schema      = N'dbo',
        @source_name        = N'Orders',
        @role_name          = NULL,
        @supports_net_changes = 1;
    PRINT 'CDC enabled on dbo.Orders';
END
ELSE
    PRINT 'CDC already enabled on dbo.Orders';
GO

-- Bước 4: Enable CDC trên bảng OrderItems
IF NOT EXISTS (
    SELECT 1 FROM cdc.change_tables
    WHERE source_object_id = OBJECT_ID('dbo.OrderItems')
)
BEGIN
    EXEC sys.sp_cdc_enable_table
        @source_schema      = N'dbo',
        @source_name        = N'OrderItems',
        @role_name          = NULL,
        @supports_net_changes = 0;
    PRINT 'CDC enabled on dbo.OrderItems';
END
ELSE
    PRINT 'CDC already enabled on dbo.OrderItems';
GO

-- Verify
SELECT
    t.name             AS table_name,
    c.capture_instance,
    c.start_lsn,
    c.create_date
FROM cdc.change_tables c
JOIN sys.tables t ON t.object_id = c.source_object_id;
GO
