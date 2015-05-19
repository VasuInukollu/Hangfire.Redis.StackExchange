﻿using Hangfire.Common;
using Hangfire.States;
using NSubstitute;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Hangfire.Redis.StackExchange.Tests
{
    [Collection("Redis")]
    public class RedisWriteOnlyTransactionFacts
    {
        private static RedisFixture Redis;
        private readonly string Prefix;

        public RedisWriteOnlyTransactionFacts(RedisFixture _Redis)
        {
            Redis = _Redis;
            Prefix = Redis.Storage.Prefix;
        }

        [Fact]
        public void Ctor_ThrowsAnException_WhenTransactionIsNull()
        {
            Assert.Throws<ArgumentNullException>(() => new RedisWriteOnlyTransaction(null, null, null));
        }

        [Fact, CleanRedis]
        public void ExpireJob_SetsExpirationDateForAllRelatedKeys()
        {
            UseConnection(redis =>
            {
                // Arrange
                redis.StringSet(Prefix + "job:my-job", "job");
                redis.StringSet(Prefix + "job:my-job:state", "state");
                redis.StringSet(Prefix + "job:my-job:history", "history");

                // Act
                Commit(redis, x => x.ExpireJob("my-job", TimeSpan.FromDays(1)));

                // Assert
                var jobEntryTtl = redis.KeyTimeToLive(Prefix + "job:my-job");
                var stateEntryTtl = redis.KeyTimeToLive(Prefix + "job:my-job:state");
                var historyEntryTtl = redis.KeyTimeToLive(Prefix + "job:my-job:state");

                Assert.True(TimeSpan.FromHours(23) < jobEntryTtl && jobEntryTtl < TimeSpan.FromHours(25));
                Assert.True(TimeSpan.FromHours(23) < stateEntryTtl && stateEntryTtl < TimeSpan.FromHours(25));
                Assert.True(TimeSpan.FromHours(23) < historyEntryTtl && historyEntryTtl < TimeSpan.FromHours(25));
            });
        }

        [Fact, CleanRedis]
        public void SetJobState_ModifiesJobEntry()
        {
            UseConnection(redis =>
            {
                // Arrange
                var state = Substitute.For<IState>();
                state.SerializeData().Returns(new Dictionary<string, string>());
                state.Name.Returns("my-state");
                state.Reason.Returns("my-reason");

                // Act
                Commit(redis, x => x.SetJobState("my-job", state));

                // Assert
                var hash = redis.HashGetAll(Prefix + "job:my-job");
                Assert.Equal("my-state", hash[0].Value);
            });
        }

        [Fact, CleanRedis]
        public void SetJobState_RewritesStateEntry()
        {
            UseConnection(redis =>
            {
                // Arrange
                redis.HashSet(Prefix + "job:my-job:state", "OldName", "OldValue");

                var state = Substitute.For<IState>();
                state.SerializeData().Returns(new Dictionary<string, string>() { { "Name", "Value" } });
                state.Name.Returns("my-state");
                state.Reason.Returns("my-reason");

                // Act
                Commit(redis, x => x.SetJobState("my-job", state));

                // Assert
                var stateHash = redis.HashGetAll(Prefix + "job:my-job:state").ToDictionary(x => x.Name, x => x.Value);
                Assert.False(stateHash.ContainsKey("OldName"));
                //Assert.Equal("my-state", stateHash["State"]); TODO: Why should this be true?
                Assert.Equal("my-reason", stateHash["Reason"]);
                Assert.Equal("Value", stateHash["Name"]);
            });
        }

        [Fact, CleanRedis]
        public void SetJobState_AppendsJobHistoryList()
        {
            UseConnection(redis =>
            {
                // Arrange
                var state = Substitute.For<IState>();
                state.Name.Returns("my-state");
                state.SerializeData().Returns(new Dictionary<string, string>());

                // Act
                Commit(redis, x => x.SetJobState("my-job", state));

                // Assert
                Assert.Equal(1, redis.ListLength(Prefix + "job:my-job:history"));
            });
        }

        [Fact, CleanRedis]
        public void PersistJob_RemovesExpirationDatesForAllRelatedKeys()
        {
            UseConnection(redis =>
            {
                // Arrange
                redis.StringSet(Prefix + "job:my-job", "job", TimeSpan.FromDays(1));
                redis.StringSet(Prefix + "job:my-job:state", "state", TimeSpan.FromDays(1));
                redis.StringSet(Prefix + "job:my-job:history", "history", TimeSpan.FromDays(1));

                // Act
                Commit(redis, x => x.PersistJob("my-job"));

                // Assert
                Assert.Null(redis.KeyTimeToLive(Prefix + "job:my-job"));
                Assert.Null(redis.KeyTimeToLive(Prefix + "job:my-job:state"));
                Assert.Null(redis.KeyTimeToLive(Prefix + "job:my-job:history"));
            });
        }

        [Fact, CleanRedis]
        public void AddJobState_AddsJobHistoryEntry_AsJsonObject()
        {
            UseConnection(redis =>
            {
                // Arrange
                var state = Substitute.For<IState>();
                state.Name.Returns("my-state");
                state.Reason.Returns("my-reason");
                state.SerializeData().Returns(
                    new Dictionary<string, string> { { "Name", "Value" } });

                // Act
                Commit(redis, x => x.AddJobState("my-job", state));

                // Assert
                var serializedEntry = redis.ListGetByIndex(Prefix + "job:my-job:history", 0);
                Assert.NotNull(serializedEntry);

                var entry = JobHelper.FromJson<Dictionary<string, string>>(serializedEntry);
                Assert.Equal("my-state", entry["State"]);
                Assert.Equal("my-reason", entry["Reason"]);
                Assert.Equal("Value", entry["Name"]);
                Assert.True(entry.ContainsKey("CreatedAt"));
            });
        }

        [Fact, CleanRedis]
        public void AddToQueue_AddsSpecifiedJobToTheQueue()
        {
            UseConnection(redis =>
            {
                Commit(redis, x => x.AddToQueue("critical", "my-job"));

                Assert.Equal(0, redis.SortedSetRank(Prefix + "queues", "critical"));
                Assert.Equal("my-job", redis.ListGetByIndex(Prefix + "queue:critical", 0));
            });
        }

        [Fact, CleanRedis]
        public void AddToQueue_PrependsListWithJob()
        {
            UseConnection(redis =>
            {
                redis.ListRightPush(Prefix + "queue:critical", "another-job");

                Commit(redis, x => x.AddToQueue("critical", "my-job"));

                Assert.Equal("my-job", redis.ListGetByIndex(Prefix + "queue:critical", 0));
            });
        }

        [Fact, CleanRedis]
        public void IncrementCounter_IncrementValueEntry()
        {
            UseConnection(redis =>
            {
                redis.StringSet(Prefix + "entry", "3");

                Commit(redis, x => x.IncrementCounter("entry"));

                Assert.Equal("4", redis.StringGet(Prefix + "entry"));
                Assert.Null(redis.KeyTimeToLive(Prefix + "entry"));
            });
        }

        [Fact, CleanRedis]
        public void IncrementCounter_WithExpiry_IncrementsValueAndSetsExpirationDate()
        {
            UseConnection(redis =>
            {
                redis.StringSet(Prefix + "entry", "3");

                Commit(redis, x => x.IncrementCounter("entry", TimeSpan.FromDays(1)));

                var entryTtl = redis.KeyTimeToLive(Prefix + "entry");
                Assert.Equal("4", redis.StringGet(Prefix + "entry"));
                Assert.True(TimeSpan.FromHours(23) < entryTtl && entryTtl < TimeSpan.FromHours(25));
            });
        }

        [Fact, CleanRedis]
        public void DecrementCounter_DecrementsTheValueEntry()
        {
            UseConnection(redis =>
            {
                redis.StringSet(Prefix + "entry", "3");

                Commit(redis, x => x.DecrementCounter("entry"));

                Assert.Equal("2", redis.StringGet(Prefix + "entry"));
                Assert.Null(redis.KeyTimeToLive("entry"));
            });
        }

        [Fact, CleanRedis]
        public void DecrementCounter_WithExpiry_DecrementsTheValueAndSetsExpirationDate()
        {
            UseConnection(redis =>
            {
                redis.StringSet(Prefix + "entry", "3");

                Commit(redis, x => x.DecrementCounter("entry", TimeSpan.FromDays(1)));

                var entryTtl = redis.KeyTimeToLive(Prefix + "entry");
                Assert.Equal("2", redis.StringGet(Prefix + "entry"));
                Assert.True(TimeSpan.FromHours(23) < entryTtl && entryTtl < TimeSpan.FromHours(25));
            });
        }

        [Fact, CleanRedis]
        public void AddToSet_AddsItemToSortedSet()
        {
            UseConnection(redis =>
            {
                Commit(redis, x => x.AddToSet("my-set", "my-value"));

                Assert.NotNull(redis.SortedSetScore(Prefix + "my-set", "my-value"));
            });
        }

        [Fact, CleanRedis]
        public void AddToSet_WithScore_AddsItemToSortedSetWithScore()
        {
            UseConnection(redis =>
            {
                Commit(redis, x => x.AddToSet("my-set", "my-value", 3.2));

                Assert.NotNull(redis.SortedSetScore(Prefix + "my-set", "my-value"));
                Assert.Equal(3.2, redis.SortedSetScore(Prefix + "my-set", "my-value"));
            });
        }

        [Fact, CleanRedis]
        public void RemoveFromSet_RemoveSpecifiedItemFromSortedSet()
        {
            UseConnection(redis =>
            {
                redis.SortedSetAdd(Prefix + "my-set", "my-value", 0);

                Commit(redis, x => x.RemoveFromSet("my-set", "my-value"));

                Assert.Null(redis.SortedSetScore(Prefix + "my-set", "my-value"));
            });
        }

        [Fact, CleanRedis]
        public void InsertToList_PrependsListWithSpecifiedValue()
        {
            UseConnection(redis =>
            {
                redis.ListRightPush(Prefix + "list", "value");

                Commit(redis, x => x.InsertToList("list", "new-value"));

                Assert.Equal("new-value", redis.ListGetByIndex(Prefix + "list", 0));
            });
        }

        [Fact, CleanRedis]
        public void RemoveFromList_RemovesAllGivenValuesFromList()
        {
            UseConnection(redis =>
            {
                redis.ListRightPush(Prefix + "list", "value");
                redis.ListRightPush(Prefix + "list", "another-value");
                redis.ListRightPush(Prefix + "list", "value");

                Commit(redis, x => x.RemoveFromList("list", "value"));

                Assert.Equal(1, redis.ListLength(Prefix + "list"));
                Assert.Equal("another-value", redis.ListGetByIndex(Prefix + "list", 0));
            });
        }

        [Fact, CleanRedis]
        public void TrimList_TrimsListToASpecifiedRange()
        {
            UseConnection(redis =>
            {
                redis.ListRightPush(Prefix + "list", "1");
                redis.ListRightPush(Prefix + "list", "2");
                redis.ListRightPush(Prefix + "list", "3");
                redis.ListRightPush(Prefix + "list", "4");

                Commit(redis, x => x.TrimList("list", 1, 2));

                Assert.Equal(2, redis.ListLength(Prefix + "list"));
                Assert.Equal("2", redis.ListGetByIndex(Prefix + "list", 0));
                Assert.Equal("3", redis.ListGetByIndex(Prefix + "list", 1));
            });
        }

        [Fact, CleanRedis]
        public void SetRangeInHash_ThrowsAnException_WhenKeyIsNull()
        {
            UseConnection(redis =>
            {
                var exception = Assert.Throws<ArgumentNullException>(
                    () => Commit(redis, x => x.SetRangeInHash(null, new Dictionary<string, string>())));

                Assert.Equal("key", exception.ParamName);
            });
        }

        [Fact, CleanRedis]
        public void SetRangeInHash_ThrowsAnException_WhenKeyValuePairsArgumentIsNull()
        {
            UseConnection(redis =>
            {
                var exception = Assert.Throws<ArgumentNullException>(
                    () => Commit(redis, x => x.SetRangeInHash("some-hash", null)));

                Assert.Equal("keyValuePairs", exception.ParamName);
            });
        }

        [Fact, CleanRedis]
        public void SetRangeInHash_SetsAllGivenKeyPairs()
        {
            UseConnection(redis =>
            {
                Commit(redis, x => x.SetRangeInHash("some-hash", new Dictionary<string, string>
                {
                    { "Key1", "Value1" },
                    { "Key2", "Value2" }
                }));

                var hash = redis.HashGetAll(Prefix + "some-hash");
                Assert.Equal("Value1", hash[0].Value);
                Assert.Equal("Value2", hash[1].Value);
            });
        }

        [Fact, CleanRedis]
        public void RemoveHash_ThrowsAnException_WhenKeyIsNull()
        {
            UseConnection(redis =>
            {
                Assert.Throws<ArgumentNullException>(
                    () => Commit(redis, x => x.RemoveHash(null)));
            });
        }

        [Fact, CleanRedis]
        public void RemoveHash_RemovesTheCorrespondingEntry()
        {
            UseConnection(redis =>
            {
                redis.HashSet(Prefix + "some-hash", "key", "value");

                Commit(redis, x => x.RemoveHash("some-hash"));

                var hash = redis.HashGetAll(Prefix + "some-hash");
                Assert.Equal(0, hash.Length);
            });
        }

        [Fact]
        private void DifferentPrefix()
        {
            UseConnection(redis =>
            {
                var SecondStorage = new RedisStorage(Redis.ServerInfo, new RedisStorageOptions { Db = Redis.Storage.Db, Prefix = "test:" });
                var SecondConnection = SecondStorage.GetDatabase();
                Commit(redis, x => x.InsertToList("some-list", "value"));
                var transaction = new RedisWriteOnlyTransaction(SecondConnection.CreateTransaction(), SecondStorage.GetSubscribe(), SecondStorage.Prefix);
                transaction.InsertToList("some-list", "value2");
                transaction.Commit();
                Assert.Equal(1, redis.ListLength(Prefix + "some-list"));
                Assert.Equal(1, SecondConnection.ListLength(SecondStorage.Prefix + "some-list"));
                Assert.Equal("value", redis.ListLeftPop(Prefix + "some-list"));
                Assert.Equal("value2", SecondConnection.ListLeftPop(SecondStorage.Prefix + "some-list"));
            });
        }

        private void Commit(IDatabase connection, Action<RedisWriteOnlyTransaction> action)
        {
            using (var transaction = new RedisWriteOnlyTransaction(connection.CreateTransaction(), Redis.Storage.GetSubscribe(), Prefix))
            {
                action(transaction);
                transaction.Commit();
            }
        }

        private void UseConnection(Action<IDatabase> action)
        {
            
            action(Redis.Storage.GetDatabase());
        }
    }
}
