﻿using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DocumentDb.Concurrency.Tests.Common;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net;

namespace DocumentDb.Concurrency.NetFull.Tests
{
	[TestClass]
	public class ConcurrentTests
	{
		private DocumentClient _dbClient;
		private readonly Uri _collectionUri;
		public ConcurrentTests()
		{
			_collectionUri = GetCollectionLink();
			//Keys for the DocDB emulator
			_dbClient = new DocumentClient(new Uri("https://localhost:8081"), "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==", new ConnectionPolicy()
			{
				MaxConnectionLimit = 100,
				ConnectionMode = ConnectionMode.Direct,
				ConnectionProtocol = Protocol.Tcp
			});
			_dbClient.OpenAsync().Wait();
		}

		/// <summary>
		/// Obtains the link of a collection
		/// </summary>
		/// <param name="databaseName"></param>
		/// <param name="collectionName"></param>
		/// <returns></returns>
		private Uri GetCollectionLink()
		{
			return UriFactory.CreateDocumentCollectionUri("test", "concurrencyTest");
		}


		private Uri GetDocumentLink(string theId)
		{
			return UriFactory.CreateDocumentUri("test", "concurrencyTest", theId);
		}

		private async Task<Database> GetOrCreateDatabaseAsync(string id)
		{
			// Get the database by name, or create a new one if one with the name provided doesn't exist.
			// Create a query object for database, filter by name.
			IEnumerable<Database> query = from db in _dbClient.CreateDatabaseQuery()
										  where db.Id == id
										  select db;

			// Run the query and get the database (there should be only one) or null if the query didn't return anything.
			// Note: this will run synchronously. If async exectution is preferred, use IDocumentServiceQuery<T>.ExecuteNextAsync.
			Database database = query.FirstOrDefault();
			if (database == null)
			{
				// Create the database.
				database = await _dbClient.CreateDatabaseAsync(new Database { Id = id });
			}

			return database;
		}

		[TestMethod]
		public async Task TheOneAndOnly()
		{
			//Creates db and collection if they dont exist
			var database = await GetOrCreateDatabaseAsync("test");
			DocumentCollection c1 = await _dbClient.CreateDocumentCollectionIfNotExistsAsync(database.SelfLink, new DocumentCollection { Id = "concurrencyTest" });

			//Creates a random document
			var theId = Guid.NewGuid().ToString();
			var aDoc = new DummyDocument();
			aDoc.Id = theId;
			await _dbClient.CreateDocumentAsync(_collectionUri, aDoc);

			//Obtains the created document
			Document theDoc = await _dbClient.ReadDocumentAsync(GetDocumentLink(theId));
			Assert.AreEqual(theDoc.Id, theId);
			
			//Modifies document altering ETag internally
			theDoc.SetPropertyValue("Property1", "modified");
			var result = await _dbClient.ReplaceConcurrentDocumentAsync(GetDocumentLink(theDoc.Id), theDoc);
			Assert.AreEqual(result.StatusCode, HttpStatusCode.OK);

			//Modifies the same document generating a concurrency exception and handling it
			theDoc.SetPropertyValue("Property1", "modified again");
			var result2 = await _dbClient.ReplaceConcurrentDocumentAsync(GetDocumentLink(theDoc.Id), theDoc).OnConcurrencyException((exception) =>
			{
				Console.WriteLine($"I got it!! {exception.Message}");
			});
			//Now with Wait (shouldnt be using it but Im testing for deadlocks)
			var result3 = _dbClient.ReplaceConcurrentDocumentAsync(GetDocumentLink(theDoc.Id), theDoc).OnConcurrencyException((exception) =>
			{
				Console.WriteLine($"I got it!! {exception.Message}");
			});
			result3.Wait();
		}
	}
}
