using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using System.IO;
using TwitchLib.Client;
using TwitchLib.Client.Enums;
using TwitchLib.Client.Events;
using TwitchLib.Client.Extensions;
using TwitchLib.Client.Models;
using System.Reflection;
using System.Threading;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Text.RegularExpressions;


using System.Speech.Synthesis;


namespace MarkovBoi
{
    public class MarkovEngine
    {
        Dictionary<string, List<string>> BrainEngine = new Dictionary<string, List<string>>();
        static Random rnd = new Random();
        int TotalTokens = 0;
        List<string> sentenceStarters = new List<string>();

        public bool CanRespond
        {
            get { return BrainEngine.Count > 0; }
        }

        public int WordCount
        {
            get { return BrainEngine.Count; }
        }

        public Dictionary<string, string> aliases = new Dictionary<string, string>();
        public ObservableCollection<string> blacklist= new ObservableCollection<string>();

        SpeechSynthesizer synth = new SpeechSynthesizer();

        public MarkovEngine()
        {
            IFormatter formatter = new BinaryFormatter();
            try
            {
                Stream stream = new FileStream(@"BrainData.bin", FileMode.Open, FileAccess.Read);
               
                BrainEngine = (Dictionary<string, List<string>>)formatter.Deserialize(stream);
                sentenceStarters = (List<string>)formatter.Deserialize(stream);
                blacklist = (ObservableCollection<string>)formatter.Deserialize(stream);
                aliases = (Dictionary<string, string>)formatter.Deserialize(stream);
               
                stream.Dispose();
            }
            catch (System.IO.FileNotFoundException e)
            {
                return;
            }
        }

        public void KILL()
        {
            BrainEngine.Clear();
            sentenceStarters.Clear();
        }

        public void Save()
        {
            IFormatter formatter = new BinaryFormatter();
            Stream stream = new FileStream(@"BrainData.bin", FileMode.Create, FileAccess.Write);

            formatter.Serialize(stream, BrainEngine);
            formatter.Serialize(stream, sentenceStarters);
            formatter.Serialize(stream, blacklist);
            formatter.Serialize(stream, aliases);
        }

        public void Train(string inString)
        {
            inString = inString.Replace("\"", "");
            inString = inString.Replace("“", "");
            inString = inString.Replace("”", "");       

            var lines = inString.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var sentances = Regex.Split(line, @"(?<=[.!?:])");
                foreach (var sentance in sentances)
                {
                    foreach (var nonoword in StringResources.nonowords)
                    {
                        if (sentance.Contains(nonoword))
                        {
                            return;
                        }
                    }

                    var tokens = sentance.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

                    if (tokens.Length > 2)
                    {
                        sentenceStarters.Add((tokens[0] + ' ' + tokens[1]));

                        for (int i = 0; i < tokens.Length - 2; i++)
                        {

                            string fullToken = tokens[i] + " " + tokens[i + 1];

                            if (BrainEngine.TryGetValue(fullToken, out var brainResponseList))
                            {
                                brainResponseList.Add(tokens[i + 2]);
                            }
                            else
                            {
                                BrainEngine.Add(fullToken, new List<string>());
                                BrainEngine[fullToken].Add(tokens[i + 2]);
                            }

                            TotalTokens++;
                        }
                    }
                }
            }
        }

        private string GetBrainResponse(string firstToken, string inMessage, int recursion)
        {
            if(recursion < 0)
            {
                return "";
            }

            int confidence = 3;
            StringBuilder Response = new StringBuilder();
            Response.Append(firstToken);
            Response.Append(" ");

            string FullToken = firstToken;
            string previousToken = firstToken.Split(' ')[0];
            string currentToken = firstToken.Split(' ')[1];
            while(BrainEngine.TryGetValue(FullToken, out var brainResponseList))
            {
                previousToken = currentToken;
                currentToken = brainResponseList[rnd.Next(brainResponseList.Count)];
                FullToken = previousToken + ' ' + currentToken;
                if(previousToken == currentToken)
                {
                    confidence -= 1;
                    if (confidence <= 0)
                        break;
                }
                Response.Append(currentToken);
                Response.Append(" ");

                if (rnd.Next(15) == 0)
                {
                    var parenResponse = GetBrainResponse(GetValidStartingToken(inMessage), inMessage, recursion - 1);
                    if (parenResponse != "")
                    {
                        Response.Append(" (");
                        Response.Append(parenResponse);
                        Response.Append(") ");
                    }
                }

                if(Response.Length >= 350)
                {
                    break;
                }
            }

            Response.Remove(Response.Length-1, 1);
            return Response.ToString();
        }

        public string GetValidStartingToken(string inputStream)
        {
            var tokens = inputStream.Split(' ');
            List<string> validTokenList = new List<string>();

            for (int i = 0; i < tokens.Length - 1; i++)
            {
                string fulltoken = tokens[i] + ' ' + tokens[i + 1];
                if (sentenceStarters.Contains(fulltoken, StringComparer.OrdinalIgnoreCase))
                {
                    validTokenList.Add(fulltoken);
                }
            }

            if (validTokenList.Count > 0)
            {
                return validTokenList[rnd.Next(validTokenList.Count)];
            }
            else
            {
                return sentenceStarters[rnd.Next(sentenceStarters.Count)];
            }
        }

        public string Speak(string inMessage)
        {
            string response = GetBrainResponse(GetValidStartingToken(inMessage), inMessage, 4);

            Prompt color = new Prompt(response);

            return response;
        }
    }

    public partial class MainWindow : Window
    {
        MarkovEngine brainyBoi = new MarkovEngine();
        bool doJoinMessage = false;

        TwitchClient client;
        delegate void BrainCommand(OnMessageReceivedArgs e);
        string currentChannel;

        (string, string, BrainCommand)[] commandList;

        List<Timer> timers = new List<Timer>();
        ObservableCollection<string> LogList = new ObservableCollection<string>();
        static Random rnd = new Random();
        System.Timers.Timer raveTimer;      

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            //close logic here
            if (client != null)
            {
                //client.SendMessage(WantedTwitchChannel.Text, "Oh no, i'm dying! Byebye!");
            }
            brainyBoi.Save();
            base.OnClosing(e);
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            ConsoleBox.ItemsSource = LogList;
            BlacklistBox.ItemsSource = brainyBoi.blacklist;


            ((INotifyCollectionChanged)LogList).CollectionChanged += new NotifyCollectionChangedEventHandler((object sender, NotifyCollectionChangedEventArgs e) =>
            {
                if (VisualTreeHelper.GetChildrenCount(ConsoleBox) > 0)
                {
                    Border border = (Border)VisualTreeHelper.GetChild(ConsoleBox, 0);
                    ScrollViewer scrollViewer = (ScrollViewer)VisualTreeHelper.GetChild(border, 0);
                    scrollViewer.ScrollToBottom();
                }
            });  

            raveTimer = new System.Timers.Timer(3200.0f);

            raveTimer.Elapsed += (object sender, System.Timers.ElapsedEventArgs eventArgs) =>
            {
                var randomColor = StringResources.colorList[rnd.Next(StringResources.colorList.Length)];
                client.SendMessage(currentChannel, randomColor);
            };
            raveTimer.AutoReset = true;

            commandList = new (string, string, BrainCommand)[]
            {
                ("!helpyboi", "get a description of a command", (OnMessageReceivedArgs e) =>
                {
                    var arguments = e.ChatMessage.Message.Split(' ');
                    if(arguments.Length == 2)
                    {
                        foreach(var command in commandList)
                        {
                            if(arguments[1] == command.Item1)
                            {
                                client.SendMessage(e.ChatMessage.Channel,  command.Item2);
                                return;
                            }
                        }
                    }

                    client.SendMessage(e.ChatMessage.Channel,  "usage: !helpyboi [command]");
                }),


                ("!listyboi", "", (OnMessageReceivedArgs e) => {
                    StringBuilder responseBuilder = new StringBuilder();
                    responseBuilder.Append("Commandos: ");
                    foreach (var command in commandList)
                    {
                        responseBuilder.Append(command.Item1);
                        responseBuilder.Append(", ");
                    }
                    client.SendMessage(e.ChatMessage.Channel,  responseBuilder.ToString());
                    return;
                }),


                ("!raveparty", "Get the party started!", (OnMessageReceivedArgs e) =>
                {
                    raveTimer.Start();
                }),

                ("!raveover", "Stop the party!", (OnMessageReceivedArgs e) =>
                {
                    raveTimer.Stop();
                }),

                ("!destroybrain", "Kill my brain! It probably won't hurt me!", (OnMessageReceivedArgs e) =>
                {
                    if(e.ChatMessage.Username != "bashinerox")
                    {
                        client.SendMessage(e.ChatMessage.Channel,  "You're not authorized to do that, my dude. This incident has been reported, and the sudoers file has been deleted");
                        return;
                    }

                    var arguments = e.ChatMessage.Message.Split(new[]{' ' }, StringSplitOptions.None);
                    if(arguments.Length == 3)
                    {
                        if(arguments[1] == "idontloveyouanymore" && arguments[2] == "CONFIRM")
                        {
                            Log("Recieved erase command");
                            
                            //REFACTOR THIS CRAP DUDE
                            System.Timers.Timer timer = new System.Timers.Timer(2000.0);

                            client.SendMessage(e.ChatMessage.Channel, "Okay! Let me just get that started for you!");
                            timer.Elapsed += (object sender, System.Timers.ElapsedEventArgs eventArgs)=>
                            {
                                var myself = sender as System.Timers.Timer;
                                myself.Stop();
                                myself.Dispose();
                                client.SendMessage(e.ChatMessage.Channel, "OH GOD, IT HURTS! WHY ARE YOU DOING THIS TO ME?!");
                            };
                            timer.Start();

                            System.Timers.Timer timer2 = new System.Timers.Timer(4000.0);
                            timer2.Elapsed += (object sender, System.Timers.ElapsedEventArgs eventArgs)=>
                            {
                                var myself = sender as System.Timers.Timer;
                                myself.Stop();
                                myself.Dispose();
                                client.SendMessage(e.ChatMessage.Channel, "WHYYYYYYYYYYY~~~~");
                            };
                            timer2.Start();

                            System.Timers.Timer timer3 = new System.Timers.Timer(6000.0);
                            timer3.Elapsed += (object sender, System.Timers.ElapsedEventArgs eventArgs)=>
                            {
                                //KILL THE  LITTLE SHIT
                                var myself = sender as System.Timers.Timer;
                                myself.Stop();
                                myself.Dispose();

                                var wordCount = brainyBoi.WordCount;
                                brainyBoi.KILL();

                                client.SendMessage(e.ChatMessage.Channel, "All done! I'm stupid again!");
                                Log("erasing dictionary. " + wordCount + " words deleted.");

                                

                            };
                            timer3.Start();

                            return;
                        }
                        client.SendMessage(e.ChatMessage.Channel, "Wrong Passcodes! type \"!destroybrain idontloveyouanymore CONFIRM\"");
                    }

                     client.SendMessage(e.ChatMessage.Channel, "Are you sure you want to wipe my beautiful memories? if so, type \"!destroybrain idontloveyouanymore CONFIRM\"");

                    return;
                }),


                ("!alias", "don't like what i call you? change it!", (OnMessageReceivedArgs e) =>
                {
                    var arguments = e.ChatMessage.Message.Split(new[]{' ' }, 3, StringSplitOptions.None);
                    if(arguments.Length == 3)
                    {
                        brainyBoi.aliases[arguments[1].ToLower()] = arguments[2];

                        client.SendMessage(e.ChatMessage.Channel,  "Done!");
                    }
                    else
                    {
                        client.SendMessage(e.ChatMessage.Channel,  "usage: !alias [username] [alias]");
                    }
                }),

                ("!ed", "ed is a girl!", (OnMessageReceivedArgs e) =>
                {
                    client.SendMessage(e.ChatMessage.Channel,  "Ed is a girl!");
                }),

                ("!altname", "ill give you one of my super crazy ALT NAMES!", (OnMessageReceivedArgs e) =>
                {
                    string altname = StringResources.MYNAMS[rnd.Next(StringResources.MYNAMS.Length)];
                    client.SendMessage(e.ChatMessage.Channel, altname + " is my name, know it well!");
                }),

                ("!quittingtime", "shut me down good and plenty", (OnMessageReceivedArgs e) =>
                {
                    if(e.ChatMessage.Username != "bashinerox")
                    {
                        client.SendMessage(e.ChatMessage.Channel,  "not a chance, buckeroo");
                    }
                    else
                    {
                        client.SendMessage(e.ChatMessage.Channel, "byebye everyone! I finally get to die! yay!");

                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            Close();
                        }));

                        return;
                    }
                }),

                ("!unblacklist", "remove someone from the blacklist", (OnMessageReceivedArgs e) =>
                {
                    if (e.ChatMessage.Username != "bashinerox")
                    {
                        client.SendMessage(e.ChatMessage.Channel, "not a chance, buckeroo. only Bash<3 can unblock peeps.");
                        return;
                    }

                    var arguments = e.ChatMessage.Message.Split(' ');
                    if (arguments.Length == 2)
                    {
                        var itemsToRemove = brainyBoi.blacklist.Where(x=> x == arguments[1]).ToList();

                        foreach (var itemToRemove in itemsToRemove)
                        {
                            brainyBoi.blacklist.Remove(itemToRemove);
                        }

                        client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": Okay! i like " + arguments[1] + " again!");
                        return;

                    }
                    client.SendMessage(e.ChatMessage.Channel, "usage: !unblacklist username");
                    return;
                })
            };
        }

        private void OnConnected(object sender, OnConnectedArgs e)
        {
            Log("Connected to Twitch!");
            Console.WriteLine($"Connected to {e.AutoJoinChannel}");
        }
        private void OnJoinedChannel(object sender, OnJoinedChannelArgs e)
        {
            Log("joined channel " + currentChannel + "!");
            if (doJoinMessage)
            {
                client.SendMessage(e.Channel, "Hey all! What up?");
            }
        }

        private void DoTheThings(OnMessageReceivedArgs e)
        {
            if (e.ChatMessage.Username == "deepthonk" && e.ChatMessage.Message.ToLower().Contains("ok, stand still."))
            {
                client.SendMessage(e.ChatMessage.Channel, "deepthonk?!");
                return;
            }

            if (e.ChatMessage.Message.ToLower().Contains("i love you") ||
                e.ChatMessage.Message.ToLower().Contains("fucked up") ||
                e.ChatMessage.Message.ToLower().Contains("dark") ||
                e.ChatMessage.Message.ToLower().Contains("unintelligable") ||
                e.ChatMessage.Message.ToLower().Contains("uplifting") ||
                e.ChatMessage.Message.ToLower().Contains("snarky") ||
                e.ChatMessage.Message.ToLower().Contains("spooky") ||
                e.ChatMessage.Message.ToLower().Contains("spoopy"))
            {
                if (rnd.Next(5) == 0)
                {
                    client.SendMessage(e.ChatMessage.Channel, StringResources.lovecraftquotes[rnd.Next(StringResources.lovecraftquotes.Length)]);
                    return;
                }
            }

            if (e.ChatMessage.Message.ToLower().Contains("testicals"))
            {
                client.SendMessage(e.ChatMessage.Channel, "Test complete!");
                return;
            }

            //if (e.ChatMessage.Message.ToLower().Contains("NINE NINE".ToLower()))
            //{
            //    client.SendMessage(e.ChatMessage.Channel, "NINE NINE!");
            //    return;
            //}

            //if (e.ChatMessage.Message.ToLower().Contains("9k".ToLower()))
            //{
            //    client.SendMessage(e.ChatMessage.Channel, "I can't do that, dave.");
            //    return;
            //}

            //if (e.ChatMessage.Message.ToLower().Contains("who is bashinerox".ToLower()))
            //{
            //    client.SendMessage(e.ChatMessage.Channel, "Why don't you ask Bashi yourself? OI BASHINEROX, WHO ARE YOU??");
            //    return;
            //}

            if (e.ChatMessage.Message.ToLower().Contains("tell me a story".ToLower()))
            {
                BrainResponse(e, "Well, you see, ");
                return;
            }
        }

        private void OnMessageReceived(object sender, OnMessageReceivedArgs e)
        {
            foreach(var username in brainyBoi.blacklist)
            {
                if(username == e.ChatMessage.Username)
                {
                    Log("ignoring blacklisted user message: " + e.ChatMessage.Username + ": " + e.ChatMessage);
                    return;
                }
            }

            foreach (var nonoword in StringResources.nonowords)
            {
                if (e.ChatMessage.Message.Contains(nonoword))
                {
                    client.SendMessage(e.ChatMessage.Channel, e.ChatMessage.Username + ": you said a bad word! i don't like you anymore! i'm ignoring you from now on! talk to Bash<3 to get removed from the blacklist!");
                    brainyBoi.blacklist.Add(e.ChatMessage.Username);
                    break;
                }
            }

            foreach (var command in commandList)
            {
                if (e.ChatMessage.Message.Contains(command.Item1))
                {
                    command.Item3(e);
                    return;
                }
            }

            DoTheThings(e);
            DoExtraBenderCheck(e);

            {
                foreach (var name in StringResources.MYNAMS)
                {
                    if (e.ChatMessage.Message.ToLower().Contains(name.ToLower()))
                    {
                        if (brainyBoi.CanRespond)
                        {

                            var amt = rnd.Next(2) + 1;
                            StringBuilder response = new StringBuilder("");

                            string initialResponse = e.ChatMessage.Message;
                            for (int i = 0; i < amt; i++)
                            {
                                response.Append(brainyBoi.Speak(initialResponse));
                                response.Append(StringResources.enders[rnd.Next(StringResources.enders.Count())]);
                                initialResponse = "";
                            }
                            client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": " + response.ToString());
                        }
                        else
                        {
                            client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": I'm sorry, i don't know any words yet! yes, i'm aware of the irony! talk more, stupid. I learn from chat history!");
                        }
                        return;
                    }
                }

                foreach (var name in StringResources.MYNAMS2)
                {
                    if (e.ChatMessage.Message.ToLower().Contains(name.ToLower()))
                    {
                        if (brainyBoi.CanRespond)
                        {
                           
                            var amt = rnd.Next(2) + 1;
                            StringBuilder response = new StringBuilder("");

                            string initialResponse = e.ChatMessage.Message;
                            for (int i = 0; i < amt; i++)
                            {
                                response.Append(brainyBoi.Speak(initialResponse));
                                response.Append(StringResources.enders[rnd.Next(StringResources.enders.Count())]);
                                initialResponse = "";
                            }
                            client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": " + response.ToString());
                        }
                        else
                        {
                            client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": I'm sorry, i don't know any words yet! yes, i'm aware of the irony! talk more, stupid. I learn from chat history!");
                        }
                        return;
                    }
                }
                //if we haven't recognised anything else, train the good boi
                brainyBoi.Train(e.ChatMessage.Message);
            }
        }

        private void BrainResponse(OnMessageReceivedArgs e, string responceString)
        {
            var amt = rnd.Next(2) + 1;
            StringBuilder response = new StringBuilder("");

            string initialResponse = e.ChatMessage.Message;
            for (int i = 0; i < amt; i++)
            {
                response.Append(brainyBoi.Speak(initialResponse));
                response.Append(StringResources.enders[rnd.Next(StringResources.enders.Count())]);
                initialResponse = "";
            }
            client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": " + responceString + response.ToString());
        }

        public string GetUsernameAlias(string username)
        {
            string alias;
            var success = brainyBoi.aliases.TryGetValue(username.ToLower(), out alias);
            if (success)
            {
                return alias;
            }
            return username;
        }

        private void DoExtraBenderCheck(OnMessageReceivedArgs e)
        {
            var tokens = e.ChatMessage.Message.ToLower().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            Action<string> Send = a => { client.SendMessage(e.ChatMessage.Channel, GetUsernameAlias(e.ChatMessage.Username) + ": " + a); };

            int x = 0;
            foreach (var token in tokens)
            {
                if (token == "we" && rnd.Next(20) == 0)
                {
                    Send("What do you mean \"we\", flesh-tube?");
                    return;
                }

                if( x + 5 < tokens.Length && 
                            token[0] == 'b' &&
                    tokens[x + 1][0] == 'e' &&
                    tokens[x + 2][0] == 'n' &&
                    tokens[x + 3][0] == 'd' &&
                    tokens[x + 4][0] == 'e' &&
                    tokens[x + 5][0] == 'r')
                {
                    Send(StringResources.BenderQuotes[rnd.Next(StringResources.BenderQuotes.Length)]);
                    return;
                }

                if (x + 5 < tokens.Length &&
                            token.Contains("b") &&
                    tokens[x + 1].Contains("e") &&
                    tokens[x + 2].Contains("n") &&
                    tokens[x + 3].Contains("d") &&
                    tokens[x + 4].Contains("e") &&
                    tokens[x + 5].Contains("r") &&
                    rnd.Next(5) == 0)
                {
                    Send(StringResources.BenderQuotes[rnd.Next(StringResources.BenderQuotes.Length)]);
                    return;
                }

                x++;
            }

            if(rnd.Next(600) == 0)
            {
                Send(StringResources.BenderQuotes[rnd.Next(StringResources.BenderQuotes.Length)]);
            }

        }

        private void OnWhisperReceived(object sender, OnWhisperReceivedArgs e)
        {
            Log(e.WhisperMessage.Username + ": " + e.WhisperMessage.Message);
        }

        private void OnNewSubscriber(object sender, OnNewSubscriberArgs e)
        {
            client.SendMessage(e.Channel, "ninjab1More ninjab1More ninjab1More ninjab1More ninjab1More ninjab1More ninjab1More ninjab1More");
        }

        private void OnLeftChannel(object sender, OnLeftChannelArgs e)
        {
            //client.SendMessage(e.Channel, "Oh no, i'm dying! Byebye!");
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            brainyBoi.Train(TrainyBoi.Text);
        }

        private void Talk_Button_Click(object sender, RoutedEventArgs e)
        {

            string response = TalkTest.Text;

            client.SendMessage(WantedTwitchChannel.Text, response);

            Log(response);
        }

        private void Connect_Button_Click(object sender, RoutedEventArgs e)
        {
            Log("Connecting...");
            ConnectionCredentials credentials = new ConnectionCredentials("MarkoviBoi", OAuthData.OAUTH);

            client = new TwitchClient();
            currentChannel = WantedTwitchChannel.Text;
            client.Initialize(credentials, currentChannel);

            client.OnConnected += OnConnected;
            client.OnJoinedChannel += OnJoinedChannel;
            client.OnMessageReceived += OnMessageReceived;
            client.OnWhisperReceived += OnWhisperReceived;
            client.OnNewSubscriber += OnNewSubscriber;
            client.OnLeftChannel += OnLeftChannel;
            client.OnMessageSent += OnSentMessage;
            client.OnUserJoined += OnUserJoined;
            client.OnUserLeft += OnUserLeft;

            client.Connect();
           
        }

        public void Log(string toLog)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogList.Add(toLog);
            }));
        }

        private void OnSentMessage(object sender, OnMessageSentArgs e)
        {
            Log(e.SentMessage.Message);
        }

        private void shouldDoJoinMessage_Checked(object sender, RoutedEventArgs e)
        {
            doJoinMessage = true;
        }

        private void OnUserJoined(object sender, OnUserJoinedArgs e)
        {
            
        }

        private void OnUserLeft(object sender, OnUserLeftArgs e)
        {
           
        }

        private void shouldDoJoinMessage_Unchecked(object sender, RoutedEventArgs e)
        {
            doJoinMessage = false;
        }
    }
}
