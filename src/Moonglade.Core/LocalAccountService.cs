﻿using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moonglade.Auditing;
using Moonglade.Data.Entities;
using Moonglade.Data.Infrastructure;
using Moonglade.Model;

namespace Moonglade.Core
{
    public class LocalAccountService : BlogService
    {
        private readonly IRepository<LocalAccountEntity> _accountRepository;
        private readonly IBlogAudit _blogAudit;

        public LocalAccountService(
            ILogger<LocalAccountService> logger,
            IRepository<LocalAccountEntity> accountRepository,
            IBlogAudit blogAudit) : base(logger)
        {
            _accountRepository = accountRepository;
            _blogAudit = blogAudit;
        }

        public int Count()
        {
            return _accountRepository.Count(p => true);
        }

        public async Task<Account> GetAsync(Guid id)
        {
            var entity = await _accountRepository.GetAsync(id);
            var item = EntityToAccountModel(entity);
            return item;
        }

        public Task<IReadOnlyList<Account>> GetAllAsync()
        {
            var list = _accountRepository.SelectAsync(p => new Account
            {
                Id = p.Id,
                CreateOnUtc = p.CreateOnUtc,
                LastLoginIp = p.LastLoginIp,
                LastLoginTimeUtc = p.LastLoginTimeUtc,
                Username = p.Username
            });

            return list;
        }

        public async Task<Guid> ValidateAsync(string username, string inputPassword)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException(nameof(username), "value must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(inputPassword))
            {
                throw new ArgumentNullException(nameof(inputPassword), "value must not be empty.");
            }

            var account = await _accountRepository.GetAsync(p => p.Username == username);
            var valid = account.PasswordHash == HashPassword(inputPassword.Trim());
            return valid ? account.Id : Guid.Empty;
        }

        public async Task LogSuccessLoginAsync(Guid id, string ipAddress)
        {
            var entity = await _accountRepository.GetAsync(id);
            if (null != entity)
            {
                entity.LastLoginIp = ipAddress.Trim();
                entity.LastLoginTimeUtc = DateTime.UtcNow;
            }

            await _accountRepository.UpdateAsync(entity);
        }

        public bool Exist(string username)
        {
            var exist = _accountRepository.Any(p => p.Username == username.ToLower());
            return exist;
        }

        public async Task<Guid> CreateAsync(string username, string clearPassword)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                throw new ArgumentNullException(nameof(username), "value must not be empty.");
            }

            if (string.IsNullOrWhiteSpace(clearPassword))
            {
                throw new ArgumentNullException(nameof(clearPassword), "value must not be empty.");
            }

            var uid = Guid.NewGuid();
            var account = new LocalAccountEntity
            {
                Id = uid,
                CreateOnUtc = DateTime.UtcNow,
                Username = username.ToLower().Trim(),
                PasswordHash = HashPassword(clearPassword.Trim())
            };

            await _accountRepository.AddAsync(account);
            await _blogAudit.AddAuditEntry(EventType.Settings, AuditEventId.SettingsAccountCreated, $"Account '{account.Id}' created.");

            return uid;
        }

        public async Task UpdatePasswordAsync(Guid id, string clearPassword)
        {
            if (string.IsNullOrWhiteSpace(clearPassword))
            {
                throw new ArgumentNullException(nameof(clearPassword), "value must not be empty.");
            }

            var account = await _accountRepository.GetAsync(id);
            if (null == account)
            {
                throw new InvalidOperationException($"LocalAccountEntity with Id '{id}' not found.");
            }

            account.PasswordHash = HashPassword(clearPassword);
            await _accountRepository.UpdateAsync(account);

            await _blogAudit.AddAuditEntry(EventType.Settings, AuditEventId.SettingsAccountPasswordUpdated, $"Account password for '{id}' updated.");
        }

        public async Task DeleteAsync(Guid id)
        {
            var account = await _accountRepository.GetAsync(id);
            if (null == account)
            {
                throw new InvalidOperationException($"LocalAccountEntity with Id '{id}' not found.");
            }

            _accountRepository.Delete(id);
            await _blogAudit.AddAuditEntry(EventType.Settings, AuditEventId.SettingsDeleteAccount, $"Account '{id}' deleted.");
        }

        public static string HashPassword(string plainMessage)
        {
            if (string.IsNullOrWhiteSpace(plainMessage))
            {
                return string.Empty;
            }

            var data = Encoding.UTF8.GetBytes(plainMessage);
            using HashAlgorithm sha = new SHA256Managed();
            sha.TransformFinalBlock(data, 0, data.Length);
            return Convert.ToBase64String(sha.Hash);
        }

        private static Account EntityToAccountModel(LocalAccountEntity entity)
        {
            if (null == entity)
            {
                return null;
            }

            return new Account
            {
                Id = entity.Id,
                CreateOnUtc = entity.CreateOnUtc,
                LastLoginIp = entity.LastLoginIp.Trim(),
                LastLoginTimeUtc = entity.LastLoginTimeUtc.GetValueOrDefault(),
                Username = entity.Username.Trim()
            };
        }
    }
}