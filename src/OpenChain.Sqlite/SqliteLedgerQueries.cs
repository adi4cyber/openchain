﻿using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SQLite;
using OpenChain.Ledger;

namespace OpenChain.Sqlite
{
    public class SqliteLedgerQueries : SqliteTransactionStore, ILedgerQueries
    {
        public SqliteLedgerQueries(string filename)
            : base(filename)
        {
        }

        public override async Task EnsureTables()
        {
            await base.EnsureTables();

            SQLiteCommand command = Connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS Accounts
                (
                    Account TEXT,
                    Asset TEXT,
                    Balance INTEGER,
                    Version BLOB,
                    PRIMARY KEY (Account ASC, Asset ASC)
                );
            ";

            await command.ExecuteNonQueryAsync();
        }

        protected override async Task AddTransaction(Mutation mutation, byte[] mutationHash)
        {
            foreach (Record record in mutation.Records)
            {
                AccountStatus account = AccountStatus.FromRecord(record);
                if (account != null)
                {
                    if (!account.Version.Equals(BinaryData.Empty))
                    {
                        await ExecuteAsync(@"
                                UPDATE  Accounts
                                SET     Balance = @balance, Version = @version
                                WHERE   Account = @account AND Asset = @asset AND Version = @previousVersion",
                            new Dictionary<string, object>()
                            {
                                { "@account", account.AccountKey.Account.FullPath },
                                { "@asset", account.AccountKey.Asset.FullPath },
                                { "@previousVersion", account.Version.Value.ToArray() },
                                { "@balance", account.Balance },
                                { "@version", mutationHash }
                            });
                    }
                    else
                    {
                        await ExecuteAsync(@"
                                INSERT INTO Accounts
                                (Account, Asset, Balance, Version)
                                VALUES (@account, @asset, @balance, @version)",
                            new Dictionary<string, object>()
                            {
                                { "@account", account.AccountKey.Account.FullPath },
                                { "@asset", account.AccountKey.Asset.FullPath },
                                { "@balance", account.Balance },
                                { "@version", mutationHash }
                            });
                    }
                }
            }
        }

        public async Task<IReadOnlyDictionary<AccountKey, AccountStatus>> GetSubaccounts(string rootAccount)
        {
             IEnumerable<AccountStatus> accounts = await ExecuteAsync(@"
                    SELECT  Account, Asset, Balance, Version
                    FROM    Accounts
                    WHERE   Account GLOB @prefix",
                reader => new AccountStatus(new AccountKey(BinaryValueUsage.Account, reader.GetString(0), reader.GetString(1)), reader.GetInt64(2), new BinaryData((byte[])reader.GetValue(3))),
                new Dictionary<string, object>()
                {
                    { "@prefix", rootAccount.Replace("[", "[[]").Replace("*", "[*]").Replace("?", "[?]") + "*" }
                });

            return new ReadOnlyDictionary<AccountKey, AccountStatus>(accounts.ToDictionary(item => item.AccountKey, item => item));
        }

        public async Task<IReadOnlyDictionary<AccountKey, AccountStatus>> GetAccount(string account)
        {
            IEnumerable<AccountStatus> accounts = await ExecuteAsync(@"
                    SELECT  Account, Asset, Balance, Version
                    FROM    Accounts
                    WHERE   Account = @account",
               reader => new AccountStatus(new AccountKey(BinaryValueUsage.Account, reader.GetString(0), reader.GetString(1)), reader.GetInt64(2), new BinaryData((byte[])reader.GetValue(3))),
               new Dictionary<string, object>()
               {
                    { "@account", account }
               });

            return new ReadOnlyDictionary<AccountKey, AccountStatus>(accounts.ToDictionary(item => item.AccountKey, item => item));
        }
    }
}