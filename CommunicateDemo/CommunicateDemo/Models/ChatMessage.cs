using System;
using System.Collections.Generic;
using System.Text;

namespace CommunicateDemo.Models
{
    public class ChatMessage
    {
        public byte[] RawData { get; set; } = Array.Empty<byte>();
        public string Sender { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsSelf { get; set; }

        /// <summary>当前显示文本（由 UI 层根据格式/编码填充）</summary>
        public string Display { get; set; } = string.Empty;
    }
}
