using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using CloudRelayService.Models;
using CloudRelayService.Helpers;

namespace CloudRelayService.Controllers
{
    public class AgentQueryController : Controller
    {
        // GET: /AgentQuery/Index/{agentId}
        public IActionResult Index(int agentId)
        {
            var collection = AgentConfigFileHelper.LoadAgentConfigs();
            var agentConfig = collection.Configs.FirstOrDefault(a => a.Id == agentId);
            if (agentConfig == null)
                return NotFound("Agent configuration not found.");
            return View(agentConfig.Queries);
        }

        // GET: /AgentQuery/Create/{agentId}
        public IActionResult Create(int agentId)
        {
            ViewBag.AgentId = agentId;
            return View();
        }

        // POST: /AgentQuery/Create/{agentId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(int agentId, StoredQuery query)
        {
            if (ModelState.IsValid)
            {
                var collection = AgentConfigFileHelper.LoadAgentConfigs();
                var agentConfig = collection.Configs.FirstOrDefault(a => a.Id == agentId);
                if (agentConfig == null)
                {
                    return NotFound("Agent configuration not found.");
                }
                query.Id = agentConfig.Queries.Any() ? agentConfig.Queries.Max(q => q.Id) + 1 : 1;
                query.CreatedOn = DateTime.UtcNow;
                agentConfig.Queries.Add(query);
                AgentConfigFileHelper.SaveAgentConfigs(collection);
                return RedirectToAction("Index", new { agentId = agentId });
            }
            ViewBag.AgentId = agentId;
            return View(query);
        }

        // GET: /AgentQuery/Edit/{agentId}/{queryId}
        public IActionResult Edit(int agentId, int queryId)
        {
            var collection = AgentConfigFileHelper.LoadAgentConfigs();
            var agentConfig = collection.Configs.FirstOrDefault(a => a.Id == agentId);
            if (agentConfig == null)
                return NotFound("Agent configuration not found.");
            var query = agentConfig.Queries.FirstOrDefault(q => q.Id == queryId);
            if (query == null)
                return NotFound("Query not found.");
            ViewBag.AgentId = agentId;
            return View(query);
        }

        // POST: /AgentQuery/Edit/{agentId}/{queryId}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int agentId, int queryId, StoredQuery updatedQuery)
        {
            if (ModelState.IsValid)
            {
                var collection = AgentConfigFileHelper.LoadAgentConfigs();
                var agentConfig = collection.Configs.FirstOrDefault(a => a.Id == agentId);
                if (agentConfig == null)
                    return NotFound("Agent configuration not found.");
                var existingQuery = agentConfig.Queries.FirstOrDefault(q => q.Id == queryId);
                if (existingQuery == null)
                    return NotFound("Query not found.");
                existingQuery.QueryText = updatedQuery.QueryText;
                existingQuery.Description = updatedQuery.Description;
                AgentConfigFileHelper.SaveAgentConfigs(collection);
                return RedirectToAction("Index", new { agentId = agentId });
            }
            ViewBag.AgentId = agentId;
            return View(updatedQuery);
        }

        // GET: /AgentQuery/Delete/{agentId}/{queryId}
        public IActionResult Delete(int agentId, int queryId)
        {
            var collection = AgentConfigFileHelper.LoadAgentConfigs();
            var agentConfig = collection.Configs.FirstOrDefault(a => a.Id == agentId);
            if (agentConfig == null)
                return NotFound("Agent configuration not found.");
            var query = agentConfig.Queries.FirstOrDefault(q => q.Id == queryId);
            if (query == null)
                return NotFound("Query not found.");
            ViewBag.AgentId = agentId;
            return View(query);
        }

        // POST: /AgentQuery/Delete/{agentId}/{queryId}
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int agentId, int queryId)
        {
            var collection = AgentConfigFileHelper.LoadAgentConfigs();
            var agentConfig = collection.Configs.FirstOrDefault(a => a.Id == agentId);
            if (agentConfig == null)
                return NotFound("Agent configuration not found.");
            var query = agentConfig.Queries.FirstOrDefault(q => q.Id == queryId);
            if (query != null)
            {
                agentConfig.Queries.Remove(query);
                AgentConfigFileHelper.SaveAgentConfigs(collection);
            }
            return RedirectToAction("Index", new { agentId = agentId });
        }
    }
}
