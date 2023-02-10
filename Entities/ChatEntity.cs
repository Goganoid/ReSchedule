using Azure;

namespace ReSchedule.Entities;

public class ChatEntity : Azure.Data.Tables.ITableEntity
{
    public long ChatId { get; set; }
    public string GroupId { get; set; } = null!;
    public bool WeekToggle { get; set; }
    public string PartitionKey { get; set; } = null!;
    public string RowKey { get; set; } = null!;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }
    public ChatEntity() { }
    public ChatEntity(long chatId, string groupId)
    {
        PartitionKey = "1";
        RowKey = Guid.NewGuid().ToString();
        GroupId = groupId;
        ChatId = chatId;
        WeekToggle = false;
    }
    public override string ToString()
    {
        return $"ChatId={ChatId} GroupId={GroupId} RowKey={RowKey}";
    }
}