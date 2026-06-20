using System;
using System.Collections.Generic;
using System.Text;

namespace D4Companion.Entities
{
    public class HOcrLine
    {
        public string Id { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int X1 { get; set; } = 0;
        public int X2 { get; set; } = 0;
        public int Y1 { get; set; } = 0;
        public int Y2 { get; set; } = 0;
    }

    public class HOcrWord
    {
        public string Id { get; set; } = string.Empty;
        public string IdLine { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public int Line { get; set; } = 0;
        public int X1 { get; set; } = 0;
        public int X2 { get; set; } = 0;
        public int Y1 { get; set; } = 0;
        public int Y2 { get; set; } = 0;        
    }
}
