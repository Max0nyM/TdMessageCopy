//
// Copyright Aliaksei Levin (levlam@telegram.org), Arseny Smirnov (arseny30@gmail.com) 2014-2020
//
// Distributed under the Boost Software License, Version 1.0. (See accompanying
// file LICENSE_1_0.txt or copy at http://www.boost.org/LICENSE_1_0.txt)
//

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data.Entity;

namespace TdMessageCopy
{
    partial class MessageCopy
    {


        public class MobileContext : DbContext
        {
            public MobileContext() : base("Data Source=|DataDirectory|DB.sdf")

            { }

            public DbSet<SaveMessage> SaveMessage { get; set; }
        }

        public class SaveMessage
        {
            [Key]
            [DatabaseGenerated(DatabaseGeneratedOption.None)]
            public Int64 messageID { get; set; }
        }
    }
}
