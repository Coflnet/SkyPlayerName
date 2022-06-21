using System.Threading.Tasks;
using Coflnet.Sky.PlayerName.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Coflnet.Sky.Core;
using System.Collections.Generic;

namespace Coflnet.Sky.PlayerName.Services
{
    public class PlayerNameService
    {
        private HypixelContext db;

        public PlayerNameService(HypixelContext db)
        {
            this.db = db;
        }

        public async Task<string> GetName(string uuid)
        {
            return await db.Players.Where(p=>p.UuId == uuid).Select(p=>p.Name).FirstOrDefaultAsync();
        }

        public async Task<string> GetUuid(string name)
        {
            return await db.Players.Where(p=>p.Name == name).Select(p=>p.UuId).FirstOrDefaultAsync();
        }
        public async Task<int> GetId(string uuid)
        {
            return await db.Players.Where(p=>p.UuId == uuid || p.Name == uuid).Select(p=>p.Id).FirstOrDefaultAsync();
        }

        internal async Task<Dictionary<string, string>> GetNames(IEnumerable<string> uuids)
        {
            return await db.Players.Where(p=>uuids.Contains(p.UuId)).ToDictionaryAsync(p=>p.UuId, p=>p.Name);
        }
    }
}
