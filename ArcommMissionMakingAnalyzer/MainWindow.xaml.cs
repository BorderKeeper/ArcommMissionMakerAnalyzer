using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using LiveCharts;
using LiveCharts.Configurations;
using LiveCharts.Wpf;

namespace ArcommMissionMakingAnalyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public const string FilePath = "archub.csv";
        public const string ArchubBotHandle = "ARCHUB#9901";
        public const string ArcmfMapName = "ARCMF";

        public const int GroupByDays = 7;

        public Func<double, string> Formatter { get; set; }
        public SeriesCollection Series { get; set; }

        public MainWindow()
        {
            InitializeComponent();

            var actions = GetActions();

            var missionSubmittedActions = actions.Where(action => action.ActionType == ActionType.MissionSubmit);

            //weed out duplicates
            missionSubmittedActions = actions.GroupBy(action => action.MapName).Select(group => group.First()).ToList();

            var mapCreators = missionSubmittedActions.GroupBy(mission => mission.ActionAuthor);
            var mapCreatorsUpdates = actions.Where(action => action.ActionType == ActionType.MissionUpdate).GroupBy(mission => mission.ActionAuthor);
            var mapTestersNotes = actions.Where(action => action.ActionType == ActionType.NoteAdded).GroupBy(mission => mission.ActionAuthor);
            var mapTesters = actions.Where(action => action.ActionType == ActionType.MissionVerify).GroupBy(mission => mission.ActionAuthor);
            var commenters = actions.Where(action => action.ActionType == ActionType.CommentAdded).GroupBy(mission => mission.ActionAuthor);

            /*DisplayTop10("Maps created",mapCreators);
            DisplayTop10("Maps updated", mapCreatorsUpdates);
            DisplayTop10("notes", mapTestersNotes);
            DisplayTop10("most verified", mapTesters);*/
            DisplayTop10("comments", commenters);

            var groupedMissions = missionSubmittedActions.GroupBy(action =>
            {
                var date = action.Date;
                date = new DateTime(date.Year, date.Month, date.Day); //remove time from datetime
                date = date.AddDays(-(date.Day % GroupByDays));

                return date;
            });

            var modelData = groupedMissions.Select(group => new MissionsCreatedModel { DateTime = group.Key, Value = group.Count() }).ToList();

            //fill gaps where mission made = 0
            //modelData = FillWeekGaps(modelData);

            var config = Mappers.Xy<MissionsCreatedModel>()
                .X(model => (double)model.DateTime.Ticks / TimeSpan.FromDays(1).Ticks)
                .Y(model => model.Value);

            Series = new SeriesCollection(config)
            {
                new LineSeries
                {
                    Title = "Maps Created",
                    Values = new ChartValues<MissionsCreatedModel>(modelData),
                    Fill = Brushes.Azure
                }
            };

            Formatter = value =>
            {
                var date = new DateTime((long) (value * TimeSpan.FromDays(1).Ticks));
                var dateString = date.ToString("d");

                if(!groupedMissions.Any(group => group.Key == date)) return dateString;

                var dataPoint = groupedMissions.First(group => group.Key == date);

                string missions = dataPoint.Select(mission => mission.MapName).Aggregate((i, j) => i + " | " + j).ToString();

                return dateString + " => " + missions;
            };

            DataContext = this;
        }

        private void DisplayTop10(string header, IEnumerable<IGrouping<string, ArchubAction>> actions)
        {
            Console.WriteLine();
            Console.WriteLine(header);
            Console.WriteLine();

            actions = actions.OrderByDescending(e => e.Count());

            int spot = 1;
            foreach (IGrouping<string, ArchubAction> action in actions)
            {
                Console.WriteLine(spot + ". " + action.Key + " | Count: " + action.Count());

                if (spot > 50) break;

                spot++;
            }
        }

        private static List<MissionsCreatedModel> FillWeekGaps(List<MissionsCreatedModel> missionSubmittedWeekly)
        {
            var firstWeek = missionSubmittedWeekly.Min(week => week.DateTime);
            var lastWeek = missionSubmittedWeekly.Max(week => week.DateTime);

            var counter = firstWeek;
            while (counter < lastWeek)
            {
                if (missionSubmittedWeekly.Any(week => week.DateTime != counter))
                {
                    missionSubmittedWeekly.Add(new MissionsCreatedModel { DateTime = counter, Value = 0 });
                }
                
                counter = counter.AddDays(GroupByDays);
            }

            return missionSubmittedWeekly.OrderBy(week => week.DateTime).ToList();
        }

        private IEnumerable<ArchubAction> GetActions()
        {
            var actions = new List<ArchubAction>();

            var comments = ReadDiscordFile();

            foreach (DiscordMessage message in comments)
            {
                var specialWords = message.Content.Split(new string[] { "**" }, StringSplitOptions.RemoveEmptyEntries);

                if(specialWords.Length < 3) continue;

                actions.Add(new ArchubAction
                {
                    ActionAuthor = specialWords[0],
                    ActionType = CastActionType(specialWords[1]),
                    FileRow = message.FileRow,
                    MapName = specialWords[2],
                    Date = message.Date
                });
            }

            actions = actions.Where(action => !action.MapName.Equals(ArcmfMapName)).ToList();

            actions = actions.OrderBy(action => action.Date).ToList();

            return actions;
        }

        private ActionType CastActionType(string input)
        {
            if (input.Contains("commented"))
            {
                return ActionType.CommentAdded;
            }

            if (input.Contains("note"))
            {
                return ActionType.NoteAdded;
            }

            if (input.Contains("updated"))
            {
                return ActionType.MissionUpdate;
            }

            if (input.Contains("submitted"))
            {
                return ActionType.MissionSubmit;
            }

            if (input.Contains("verified"))
            {
                return ActionType.MissionVerify;
            }

            return ActionType.Unknown;
        }

        private IEnumerable<DiscordMessage> ReadDiscordFile()
        {
            var messages = new List<DiscordMessage>();
            var id = 0;

            using (var reader = new StreamReader(FilePath))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();

                    if (line == string.Empty) break;

                    var values = line.Split(';');

                    var author = values[0].Trim('"');
                    if (author.Equals(ArchubBotHandle))
                    {                    
                        messages.Add(new DiscordMessage
                        {
                            FileRow = id,
                            Author = author,
                            Date = DateTime.Parse(values[1].Trim('"')),
                            Content = values[2].Trim('"')
                        });
                    }

                    id++;
                }
            }

            return messages;
        }

        //data obtained from https://github.com/Tyrrrz/DiscordChatExporter/releases
        public class DiscordMessage
        {
            public int FileRow { get; set; }

            public string Author { get; set; }

            public DateTime Date { get; set; }

            public string Content { get; set; }
        }

        public class ArchubAction
        {
            public int FileRow { get; set; }

            public DateTime Date { get; set; }

            public string ActionAuthor { get; set; }

            public ActionType ActionType { get; set; }

            public string MapName { get; set; }
        }

        public class MissionsCreatedModel
        {
            public DateTime DateTime { get; set; }

            public int Value { get; set; }
        }

        public enum ActionType
        {
            MissionSubmit,
            MissionUpdate,
            MissionVerify,
            NoteAdded,
            CommentAdded,
            Unknown
        }
    }
}
