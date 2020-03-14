//
// Copyright Aliaksei Levin (levlam@telegram.org), Arseny Smirnov (arseny30@gmail.com) 2014-2020
//
// Distributed under the Boost Software License, Version 1.0. (See accompanying
// file LICENSE_1_0.txt or copy at http://www.boost.org/LICENSE_1_0.txt)
//

using Td = Telegram.Td;
using TdApi = Telegram.Td.Api;

using System;
using System.Threading;
using System.Data.Entity;
using Telegram.Td.Api;
using System.Configuration;
using System.Linq;

namespace TdMessageCopy
{
    /// <summary>
    /// MessageCopy class for TDLib usage from C#.
    /// </summary>
    partial class MessageCopy
    {
        private static AppConfig appConfig = null;
        private static MobileContext db = null;
        private static Td.Client _client = null;
        private readonly static Td.ClientResultHandler _defaultHandler = new DefaultHandler();

        private static TdApi.AuthorizationState _authorizationState = null;
        private static volatile bool _haveAuthorization = false;
        private static volatile bool _quiting = false;

        private static volatile AutoResetEvent _gotAuthorization = new AutoResetEvent(false);

        private static readonly string _newLine = Environment.NewLine;
        private static readonly string _commandsLine = "Enter command (gc <chatId> - GetChat, me - GetMe, sm <chatId> <message> - SendMessage, lo - LogOut, r - Restart, q - Quit): ";
        private static volatile string _currentPrompt = null;


        public class AppConfig
        {           
            public int apiID { get; set; }
            public string apiHash { get; set; }
            public Int64[] chatIds { get; set; }

            public string targetChatId { get; set; }

            public AppConfig()
            {
                var reader = new AppSettingsReader();
                apiID = (int)reader.GetValue("ApiId", typeof(int));
                apiHash = (string)reader.GetValue("ApiHash", typeof(string));
                string chatString = (string)reader.GetValue("ChatId", typeof(string));
                chatIds = chatString.Split(',').Select(s => Int64.Parse(s)).ToArray();
                targetChatId = (string)reader.GetValue("TargetChatId", typeof(string));

            }

        }

              


        private static Td.Client CreateTdClient()
        {
            Td.Client result = Td.Client.Create(new UpdatesHandler());
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                result.Run();
            }).Start();
            return result;
        }

        private static void Print(string str)
        {
            if (_currentPrompt != null)
            {
                Console.WriteLine();
            }
            Console.WriteLine(str);
            if (_currentPrompt != null)
            {
                Console.Write(_currentPrompt);
            }
        }

        private static string ReadLine(string str)
        {
            Console.Write(str);
            _currentPrompt = str;
            var result = Console.ReadLine();
            _currentPrompt = null;
            return result;
        }

        private static void OnAuthorizationStateUpdated(TdApi.AuthorizationState authorizationState)
        {
            if (authorizationState != null)
            {
                _authorizationState = authorizationState;
            }
            if (_authorizationState is TdApi.AuthorizationStateWaitTdlibParameters)
            {
                TdApi.TdlibParameters parameters = new TdApi.TdlibParameters();
                parameters.DatabaseDirectory = "tdlib";
                parameters.UseMessageDatabase = true;
                parameters.UseSecretChats = true;
                parameters.ApiId = appConfig.apiID;
                parameters.ApiHash = appConfig.apiHash;
                parameters.SystemLanguageCode = "en";
                parameters.DeviceModel = "Desktop";
                parameters.SystemVersion = "Unknown";
                parameters.ApplicationVersion = "1.0";
                parameters.EnableStorageOptimizer = true;

                _client.Send(new TdApi.SetTdlibParameters(parameters), new AuthorizationRequestHandler());
            }
            else if (_authorizationState is TdApi.AuthorizationStateWaitEncryptionKey)
            {
                _client.Send(new TdApi.CheckDatabaseEncryptionKey(), new AuthorizationRequestHandler());
            }
            else if (_authorizationState is TdApi.AuthorizationStateWaitPhoneNumber)
            {
                string phoneNumber = ReadLine("Please enter phone number: ");
                _client.Send(new TdApi.SetAuthenticationPhoneNumber(phoneNumber, null), new AuthorizationRequestHandler());
            }
            else if (_authorizationState is TdApi.AuthorizationStateWaitOtherDeviceConfirmation state)
            {
                Console.WriteLine("Please confirm this login link on another device: " + state.Link);
            }
            else if (_authorizationState is TdApi.AuthorizationStateWaitCode)
            {
                string code = ReadLine("Please enter authentication code: ");
                _client.Send(new TdApi.CheckAuthenticationCode(code), new AuthorizationRequestHandler());
            }
            else if (_authorizationState is TdApi.AuthorizationStateWaitRegistration)
            {
                string firstName = ReadLine("Please enter your first name: ");
                string lastName = ReadLine("Please enter your last name: ");
                _client.Send(new TdApi.RegisterUser(firstName, lastName), new AuthorizationRequestHandler());
            }
            else if (_authorizationState is TdApi.AuthorizationStateWaitPassword)
            {
                string password = ReadLine("Please enter password: ");
                _client.Send(new TdApi.CheckAuthenticationPassword(password), new AuthorizationRequestHandler());
            }
            else if (_authorizationState is TdApi.AuthorizationStateReady)
            {
                _haveAuthorization = true;
                _gotAuthorization.Set();
            }
            else if (_authorizationState is TdApi.AuthorizationStateLoggingOut)
            {
                _haveAuthorization = false;
                Print("Logging out");
            }
            else if (_authorizationState is TdApi.AuthorizationStateClosing)
            {
                _haveAuthorization = false;
                Print("Closing");
            }
            else if (_authorizationState is TdApi.AuthorizationStateClosed)
            {
                Print("Closed");
                _client.Dispose(); // _client is closed and native resources can be disposed now
                if (!_quiting)
                {
                    _client = CreateTdClient(); // recreate _client after previous has closed
                }
            }
            else
            {
                Print("Unsupported authorization state:" + _newLine + _authorizationState);
            }
        }

        private static long GetChatId(string arg)
        {
            long chatId = 0;
            try
            {
                chatId = Convert.ToInt64(arg);
            }
            catch (FormatException)
            {
            }
            catch (OverflowException)
            {
            }
            return chatId;
        }

        private static void GetCommand()
        {
            string command = ReadLine(_commandsLine);
            string[] commands = command.Split(new char[] { ' ' }, 2);
            try
            {
                switch (commands[0])
                {
                    case "gc":
                        _client.Send(new TdApi.GetChat(GetChatId(commands[1])), _defaultHandler);
                        
                        break;
                    case "me":
                        _client.Send(new TdApi.GetMe(), _defaultHandler);
                        break;
                    case "sm":
                        string[] args = commands[1].Split(new char[] { ' ' }, 2);
                        sendMessage(GetChatId(args[0]), args[1]);
                        break;
                    case "lo":
                        _haveAuthorization = false;
                        _client.Send(new TdApi.LogOut(), _defaultHandler);
                        break;
                    case "r":
                        _haveAuthorization = false;
                        _client.Send(new TdApi.Close(), _defaultHandler);
                        break;
                    case "q":
                        _quiting = true;
                        _haveAuthorization = false;
                        _client.Send(new TdApi.Close(), _defaultHandler);
                        break;
                    default:
                        Print("Unsupported command: " + command);
                        break;
                }
            }
            catch (IndexOutOfRangeException)
            {
                Print("Not enough arguments");
            }
        }

        private static void sendMessage(long chatId, string message)
        {
            // initialize reply markup just for testing
            TdApi.InlineKeyboardButton[] row = { new TdApi.InlineKeyboardButton("https://telegram.org?1", new TdApi.InlineKeyboardButtonTypeUrl()), new TdApi.InlineKeyboardButton("https://telegram.org?2", new TdApi.InlineKeyboardButtonTypeUrl()), new TdApi.InlineKeyboardButton("https://telegram.org?3", new TdApi.InlineKeyboardButtonTypeUrl()) };
            TdApi.ReplyMarkup replyMarkup = new TdApi.ReplyMarkupInlineKeyboard(new TdApi.InlineKeyboardButton[][] { row, row, row });

            TdApi.InputMessageContent content = new TdApi.InputMessageText(new TdApi.FormattedText(message, null), false, true);
            _client.Send(new TdApi.SendMessage(chatId, 0, null, replyMarkup, content), _defaultHandler);
        }



        private static void sendMessage(long chatId, TdApi.MessagePhoto message)
        {
            // initialize reply markup just for testing
            TdApi.InlineKeyboardButton[] row = { new TdApi.InlineKeyboardButton("https://telegram.org?1", new TdApi.InlineKeyboardButtonTypeUrl()), new TdApi.InlineKeyboardButton("https://telegram.org?2", new TdApi.InlineKeyboardButtonTypeUrl()), new TdApi.InlineKeyboardButton("https://telegram.org?3", new TdApi.InlineKeyboardButtonTypeUrl()) };
            TdApi.ReplyMarkup replyMarkup = new TdApi.ReplyMarkupInlineKeyboard(new TdApi.InlineKeyboardButton[][] { row, row, row });

            TdApi.InputFileRemote inputFile = new TdApi.InputFileRemote(message.Photo.Sizes[message.Photo.Sizes.Length-1].Photo.Remote.Id);
            TdApi.InputMessageContent content = new TdApi.InputMessagePhoto(inputFile, null, null, message.Photo.Sizes[0].Width, message.Photo.Sizes[0].Height, message.Caption, 0);

            _client.Send(new TdApi.SendMessage(chatId, 0, null, replyMarkup, content), _defaultHandler);


        }

        static void Main()
        {
            

            appConfig = new AppConfig();
           
            db = new MobileContext();
            // disable TDLib log
            Td.Client.Execute(new TdApi.SetLogVerbosityLevel(0));
            if (Td.Client.Execute(new TdApi.SetLogStream(new TdApi.LogStreamFile("tdlib.log", 1 << 27))) is TdApi.Error)
            {
                throw new System.IO.IOException("Write access to the current directory is required");
            }

            // create Td.Client
            _client = CreateTdClient();

            // test Client.Execute
            _defaultHandler.OnResult(Td.Client.Execute(new TdApi.GetTextEntities("@telegram /test_command https://telegram.org telegram.me @gif @test")));

            var exitTime = DateTime.Now.AddMinutes(5);

            // main loop
            while (!_quiting)
            {
                // await authorization
                _gotAuthorization.Reset();
                _gotAuthorization.WaitOne();

                _client.Send(new TdApi.GetChats(null, Int64.MaxValue, 0, 100), _defaultHandler); // preload main chat list
                while (_haveAuthorization)
                {
                    GetCommand();
                }
            }
        }

        private class DefaultHandler : Td.ClientResultHandler
        {
            void Td.ClientResultHandler.OnResult(TdApi.BaseObject @object)
            {
                Print(@object.ToString());
               
                //   sendMessage(GetChatId("350715255"), @object.ToString());
            }
        }

        private class UpdatesHandler : Td.ClientResultHandler
        {
            void Td.ClientResultHandler.OnResult(TdApi.BaseObject @object)
            {
                if (@object is TdApi.UpdateAuthorizationState)
                {
                    OnAuthorizationStateUpdated((@object as TdApi.UpdateAuthorizationState).AuthorizationState);
                }
                else
                {
                     //  Print("Unsupported update: " + @object);
                    if (@object is TdApi.UpdateChatLastMessage)
                    {
                        ///RDV - -1001408447562
                        ///HIDE -1001352498778
                        ///my -1001260330650
                        TdApi.Message lastMessage = (@object as TdApi.UpdateChatLastMessage).LastMessage;
                        if (lastMessage != null && appConfig.chatIds.Contains(lastMessage.ChatId))
                        {
                            var saveMessage = new SaveMessage();
                            saveMessage.messageID = lastMessage.Id;
                            if (db.SaveMessage.Find(lastMessage.Id) == null)
                            {
                                db.SaveMessage.Add(saveMessage);
                                db.SaveChanges();
                                sendMessage(lastMessage);
                            //    Print(lastMessage.ToString());
                            }


                        }
                    }
                }
            }

            private static void sendMessage(Message lastMessage)
            {
                switch (lastMessage.Content)
                {
                    case TdApi.MessageText message:
                        MessageCopy.sendMessage(GetChatId(appConfig.targetChatId), message.Text.Text);
                        break;
                    case TdApi.MessagePhoto photo:
                        MessageCopy.sendMessage(GetChatId(appConfig.targetChatId), photo);
                        break;
                    default:
                        break;
                }
            }
        }

        private class AuthorizationRequestHandler : Td.ClientResultHandler
        {
            void Td.ClientResultHandler.OnResult(TdApi.BaseObject @object)
            {
                if (@object is TdApi.Error)
                {
                    Print("Receive an error:" + _newLine + @object);
                    OnAuthorizationStateUpdated(null); // repeat last action
                }
                else
                {
                    // result is already received through UpdateAuthorizationState, nothing to do
                }
            }
        }
    }
}
