﻿using EvangelionERPV2.Domain.Interfaces;
using EvangelionERPV2.Infra.Context;

namespace EvangelionERPV2.Infra.Repositories
{
    public class UnitOfWork<TContext> : IUnitOfWork<TContext> where TContext : AppDbContext
    {
        private readonly AppDbContext _context;

        public UnitOfWork(AppDbContext context)
        {
            _context = context;
        }

        public void Commit(CancellationToken cancellationToken = default)
        {
            if (_context.ChangeTracker.HasChanges())
                _context.SaveChanges();
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            if (_context.ChangeTracker.HasChanges())
                await _context.SaveChangesAsync();
        }
    }
}