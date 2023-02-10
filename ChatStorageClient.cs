using Azure;
using Azure.Data.Tables;
using Microsoft.Extensions.Logging;
using ReSchedule.Entities;
namespace ReSchedule;

public class ChatStorageClient
{
    private readonly TableClient _tableClient;
    private readonly ILogger _logger;
    public ChatStorageClient(ILogger<ChatStorageClient> logger)
    {
        _logger = logger;
        _logger.LogInformation("Creating ChatStorageClient instance");
        var connStr = Environment.GetEnvironmentVariable("ConnectionStrings:TableConnection");
        var tableName = Environment.GetEnvironmentVariable("TableName");
        if (connStr == null)
        {
            _logger.LogError($"Development connection string is null");
            _logger.LogInformation("Trying production connection string");
            connStr = Environment.GetEnvironmentVariable("CUSTOMCONNSTR_TableConnection");
            if(connStr==null)
            {
                _logger.LogError($"Production Connection string is also null");
                throw new Exception("Connection string is not set");
            }
            
        }

        if (tableName == null)
        {
            _logger.LogError($"TableName is not set");
            throw new Exception("TableName is not set");
        }
        _tableClient = new TableClient(connStr, tableName);
    }

    public void SetupTable()
    {
        _tableClient.CreateIfNotExists();
    }
    
    public async Task<ChatEntity?> GetChatEntityAsync(long chatId)
    {
        var chats =  _tableClient
            .QueryAsync<ChatEntity>(c => c.ChatId == chatId);
        var enumerator = chats.GetAsyncEnumerator();
        await enumerator.MoveNextAsync();
        var chat = enumerator.Current;
        if(chat==null) _logger.LogWarning("Requested chat {} is null", chatId);
        return chat;
    }


    public async Task SetChatEntityAsync(ChatEntity newChatEntity)
    {
        _logger.LogInformation("Setting chat entity. ChatId={}",newChatEntity.ChatId);
        var existingChatEntity = await GetChatEntityAsync(newChatEntity.ChatId);
        if (existingChatEntity != null)
        {
            _logger.LogInformation("Updating existing entity. ChatId={}",newChatEntity.ChatId);
            existingChatEntity.GroupId = newChatEntity.GroupId;
            existingChatEntity.WeekToggle = newChatEntity.WeekToggle;
            try
            {
                await _tableClient.UpdateEntityAsync(existingChatEntity, ETag.All);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error happened while updating entity. Entity={}\nError={}",newChatEntity,ex.Message);
            }
        }
        else
        {
            _logger.LogInformation("Creating new enity. Entity={}",newChatEntity);
            await _tableClient.AddEntityAsync(newChatEntity);
        }
    }
}