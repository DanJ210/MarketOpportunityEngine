SET NOCOUNT ON;

IF DB_NAME() <> N'MarketOpportunityEngine_LiveValidationTests'
    THROW 50000, 'This query must run against the dedicated live validation test database.', 1;

DECLARE @SessionId nvarchar(64) = N'$(SessionId)';

SELECT DB_NAME() AS ValidationDatabase, @SessionId AS SessionId;

SELECT N'CanonicalEvents' AS RecordType, COUNT_BIG(*) AS PersistedCount
FROM dbo.market_event WHERE SessionId = @SessionId
UNION ALL
SELECT N'FeatureSnapshots', COUNT_BIG(*)
FROM dbo.feature_snapshot WHERE SessionId = @SessionId
UNION ALL
SELECT N'OpportunityEvaluations', COUNT_BIG(*)
FROM dbo.opportunity WHERE SessionId = @SessionId;

SELECT N'CanonicalEvents' AS RecordType, Symbol, COUNT_BIG(*) AS PersistedCount
FROM dbo.market_event WHERE SessionId = @SessionId GROUP BY Symbol
UNION ALL
SELECT N'FeatureSnapshots', Symbol, COUNT_BIG(*)
FROM dbo.feature_snapshot WHERE SessionId = @SessionId GROUP BY Symbol
UNION ALL
SELECT N'OpportunityEvaluations', Symbol, COUNT_BIG(*)
FROM dbo.opportunity WHERE SessionId = @SessionId GROUP BY Symbol
ORDER BY RecordType, Symbol;

SELECT EventType, COUNT_BIG(*) AS PersistedCount,
    MAX(EventTimeUtc) AS LastProviderEventTimeUtc,
    MAX(ReceivedTimeUtc) AS LastReceiveTimeUtc
FROM dbo.market_event WHERE SessionId = @SessionId GROUP BY EventType;

SELECT DataProvider, DataFeed, DataCoverage, Classification,
    COUNT_BIG(*) AS PersistedCount
FROM dbo.opportunity WHERE SessionId = @SessionId
GROUP BY DataProvider, DataFeed, DataCoverage, Classification;
