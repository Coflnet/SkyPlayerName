using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Coflnet.Sky.PlayerName.Models;
using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Collections;
using System.Collections.Generic;
using Coflnet.Sky.PlayerName.Services;

namespace Coflnet.Sky.PlayerName.Controllers
{
    /// <summary>
    /// Main Controller handling tracking
    /// </summary>
    [ApiController]
    [Route("[controller]")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
    public class PlayerNameController : ControllerBase
    {
        private readonly PlayerNameService service;
        private readonly ILogger<PlayerNameController> logger;

        /// <summary>
        /// Creates a new instance of <see cref="PlayerNameController"/>
        /// </summary>
        /// <param name="service"></param>
        public PlayerNameController(PlayerNameService service, ILogger<PlayerNameController> logger)
        {
            this.service = service;
            this.logger = logger;
        }

        /// <summary>
        /// Gets the name for a uuid
        /// </summary>
        /// <param name="uuid">The uuid to get the name for</param>
        /// <returns></returns>
        [HttpGet]
        [Route("name/{uuid}")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<string> GetName(string uuid)
        {
            logger.LogInformation($"Getting {uuid}");
            return await service.GetName(uuid);
        }

        /// <summary>
        /// Gets the name for a uuid
        /// </summary>
        /// <param name="uuids">The uuid to get the name for</param>
        /// <returns></returns>
        [HttpPost]
        [Route("names/batch")]
        public async Task<Dictionary<string,string>> GetNameBatch(IEnumerable<string> uuids)
        {
            return await service.GetNames(uuids);
        }
        
        /// <summary>
        /// Gets the uuid for some name
        /// </summary>
        /// <param name="name">The name to get the uuid for</param>
        /// <returns></returns>
        [HttpGet]
        [Route("uuid/{name}")]
        [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any, NoStore = false)]
        public async Task<string> GetUuId(string name)
        {
            return await service.GetUuid(name);
        }
    }
}
