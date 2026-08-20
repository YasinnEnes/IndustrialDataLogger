using IndustrialMonitoring.API.Data;
using IndustrialMonitoring.API.Models.Entities;
using IndustrialMonitoring.API.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IndustrialMonitoring.API.Repositories
{
    public class SensorRepository : IRepository<SensorData>
    {
        private readonly AppDbContext _context;

        public SensorRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<SensorData>> GetAllAsync()
        {
            return await _context.SensorDataSet.ToListAsync();
        }

        public async Task<SensorData?> GetByIdAsync(int id)
        {
            return await _context.SensorDataSet.FindAsync(id);
        }

        public async Task AddAsync(SensorData entity)
        {
            await _context.SensorDataSet.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(SensorData entity)
        {
            _context.SensorDataSet.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await GetByIdAsync(id);
            if (entity != null)
            {
                _context.SensorDataSet.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }
    }
}