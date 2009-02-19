using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using ThoughtWorks.CruiseControl.Core.Tasks;
using ThoughtWorks.CruiseControl.Core.Util;
using ThoughtWorks.CruiseControl.Remote;

namespace ThoughtWorks.CruiseControl.Core
{
    /// <summary>
    /// Contains all the results of a project's integration.
    /// </summary>
    [Serializable]
    public class IntegrationResult : IIntegrationResult
    {
        public const string InitialLabel = "UNKNOWN";

        // immutable properties
        private string projectName;
        private string projectUrl;
        private string workingDirectory;
        private string artifactDirectory = "";
        private IntegrationRequest request = IntegrationRequest.NullRequest;
        private IntegrationSummary lastIntegration = IntegrationSummary.Initial;
        private string buildLogDirectory;


        // mutable properties
        private IntegrationStatus status = IntegrationStatus.Unknown;
        private ArrayList failureUsers = new ArrayList();
        private string label = InitialLabel;
        private DateTime startTime;
        private DateTime endTime;
        private Modification[] modifications = new Modification[0];
        private Exception exception;

        private readonly List<ITaskResult> taskResults = new List<ITaskResult>();

        private readonly BuildProgressInformation buildProgressInformation = new BuildProgressInformation("", "");

        private bool sourceControlErrorOccured = false;


        [XmlIgnore]
        public BuildProgressInformation BuildProgressInformation
        {
            get { return buildProgressInformation; }
        }

        // Default constructor required for serialization
        public IntegrationResult()
        {
        }

        public IntegrationResult(string projectName, string workingDirectory, string artifactDirectory, IntegrationRequest request, IntegrationSummary lastIntegration)
        {
            ProjectName = projectName;
            WorkingDirectory = workingDirectory;
            ArtifactDirectory = artifactDirectory;
            this.request = (lastIntegration.IsInitial()) ? new IntegrationRequest(BuildCondition.ForceBuild, request.Source) : request;
            this.lastIntegration = lastIntegration;
            if ((lastIntegration.Status == IntegrationStatus.Exception)
                || (lastIntegration.Status == IntegrationStatus.Failure))
                failureUsers = lastIntegration.FailureUsers;       // Inherit the previous build's failureUser list if it failed.

            buildProgressInformation = new BuildProgressInformation(artifactDirectory, projectName);
            
            
            this.label = this.LastIntegration.Label;

        }

        public string ProjectName
        {
            get { return projectName; }
            set { projectName = value; }
        }

        public string ProjectUrl
        {
            get { return projectUrl; }
            set { projectUrl = value; }
        }

        public BuildCondition BuildCondition
        {
            get { return request.BuildCondition; }
            set { request = new IntegrationRequest(value, "reloaded from state file"); }
        }

        public string Label
        {
            get { return label; }
            set { label = value; }
        }

        /// <summary>
        /// Obtain the label as an integer if possible, otherwise zero. 
        /// </summary>
        /// <remarks>
        // "0" is better than "-1" since build numbers are non-negative
        /// and "-" is a character frequently used to separate version components
        /// when represented in string form.  Thus "-1" might give someone
        /// "1-0--1", which might cause all sorts of havoc for them.  Best to
        /// avoid the "-" character.
        /// </remarks>
        public int NumericLabel
        {
            get
            {
                try
                {
                    string tempNumericLabel = Regex.Replace(Label, @".*?(\d+$)", "$1");
                    return int.Parse(tempNumericLabel);
                }
                catch (FormatException)
                {
                    return 0;
                }
            }
        }

        public string WorkingDirectory
        {
            get { return workingDirectory; }
            set { workingDirectory = value; }
        }

        public string ArtifactDirectory
        {
            get { return artifactDirectory; }
            set { artifactDirectory = value; }
        }


        public string BuildLogDirectory
        {
            get { return buildLogDirectory; }
            set { buildLogDirectory = value; }
        }


        public string IntegrationArtifactDirectory
        {
            get { return Path.Combine(ArtifactDirectory, Label); }
        }

        public string ListenerFile
        {
            get
            {
                return Path.Combine(artifactDirectory,
                  StringUtil.RemoveInvalidCharactersFromFileName(projectName) + "_ListenFile.xml");
            }
        }

        public IntegrationStatus Status
        {
            get { return status; }
            set { status = value; }
        }

        /// <summary>
        /// Gets and sets the date and time at which the integration commenced.
        /// </summary>
        public DateTime StartTime
        {
            get { return startTime; }
            set { startTime = value; }
        }

        /// <summary>
        /// Gets and sets the date and time at which the integration was completed.
        /// </summary>
        public DateTime EndTime
        {
            get { return endTime; }
            set { endTime = value; }
        }

        [XmlIgnore]
        public virtual Modification[] Modifications
        {
            get { return modifications; }
            set { modifications = value; }
        }

        public DateTime LastModificationDate
        {
            get
            {
                //TODO: why set the date to yesterday's date as a default
                if (Modifications.Length == 0)
                {
                    //If there are no modifications then this should be set to the last modification date
                    // from the last integration (or 1/1/1980 if there is no previous integration).
                    return DateTime.Now.AddDays(-1.0);
                }

                DateTime latestDate = DateTime.MinValue;
                foreach (Modification modification in Modifications)
                {
                    latestDate = DateUtil.MaxDate(modification.ModifiedTime, latestDate);
                }
                return latestDate;
            }
        }

        public int LastChangeNumber
        {
            get
            {
                return Modification.GetLastChangeNumber(modifications);
            }
        }

        public bool IsInitial()
        {
            return Label == InitialLabel;
        }

        /// <summary>
        /// Gets a value indicating the success of this integration.
        /// </summary>
        public bool Succeeded
        {
            get { return Status == IntegrationStatus.Success; }
        }

        /// <summary>
        /// Gets a value indicating whether this integration failed.
        /// </summary>
        public bool Failed
        {
            get { return Status == IntegrationStatus.Failure; }
        }

        /// <summary>
        /// Gets a value indicating whether this integration fixed a previously broken build.
        /// </summary>
        public bool Fixed
        {
            get { return Succeeded && LastIntegrationStatus == IntegrationStatus.Failure; }
        }

        /// <summary>
        /// Gets the time taken to perform the project's integration.
        /// </summary>
        public TimeSpan TotalIntegrationTime
        {
            get { return EndTime - StartTime; }
        }

        // Exceptions cannot be serialised because of permission attributes
        [XmlIgnore]
        public Exception ExceptionResult
        {
            get { return exception; }
            set
            {
                exception = value;
                if (exception != null)
                    Status = IntegrationStatus.Exception;
            }
        }

        [XmlIgnore]
        public IList TaskResults
        {
            get { return taskResults; }
        }

        public void AddTaskResult(string result)
        {
            AddTaskResult(new DataTaskResult(result));
        }

        public void AddTaskResult(ITaskResult result)
        {
            taskResults.Add(result);
            if (Failed || Status == IntegrationStatus.Exception)
                return;

            Status = result.Succeeded() ? IntegrationStatus.Success : IntegrationStatus.Failure;
        }

        public void MarkStartTime()
        {
            StartTime = DateTime.Now;
        }

        public void MarkEndTime()
        {
            EndTime = DateTime.Now;
        }

        public bool HasModifications()
        {
            return Modifications.Length > 0;
        }

        public static IntegrationResult CreateInitialIntegrationResult(string project, string workingDirectory, string artifactDirectory)
        {
            IntegrationRequest initialRequest = new IntegrationRequest(BuildCondition.ForceBuild, "Initial Build");
            IntegrationResult result = new IntegrationResult(project, workingDirectory, artifactDirectory, initialRequest, IntegrationSummary.Initial);
            result.StartTime = DateTime.Now.AddDays(-1);
            result.EndTime = DateTime.Now;
            return result;
        }

        /// <summary>
        /// Determines whether a build should run.  A build should run if there
        /// are modifications, and none have occurred within the modification
        /// delay.
        /// </summary>
        public bool ShouldRunBuild()
        {
            return BuildCondition.ForceBuild == BuildCondition || HasModifications();
        }

        public string BaseFromArtifactsDirectory(string pathToBase)
        {
            return string.IsNullOrEmpty(pathToBase) ? ArtifactDirectory : Path.Combine(ArtifactDirectory, pathToBase);
        }

        public string BaseFromWorkingDirectory(string pathToBase)
        {
            return string.IsNullOrEmpty(pathToBase) ? WorkingDirectory : Path.Combine(WorkingDirectory, pathToBase);
        }

        /// <summary>
        /// Contains the output from the build process.  In the case of NAntBuilder, this is the 
        /// redirected StdOut of the nant.exe process.
        /// </summary>
        [XmlIgnore]
        public string TaskOutput
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                foreach (ITaskResult result in taskResults)
                    sb.Append(result.Data);

                return sb.ToString();
            }
        }

        [XmlIgnore]
        public IntegrationSummary LastIntegration
        {
            get { return lastIntegration; }
        }

        public IntegrationStatus LastIntegrationStatus
        {
            get { return lastIntegration.Status; }
            set { lastIntegration = new IntegrationSummary(value, lastIntegration.Label, LastSuccessfulIntegrationLabel, lastIntegration.StartTime); }		// used only for loading IntegrationResult from state file - to be removed
        }

        public string LastSuccessfulIntegrationLabel
        {
            get { return (Succeeded || lastIntegration.LastSuccessfulIntegrationLabel == null) ? Label : lastIntegration.LastSuccessfulIntegrationLabel; }
            set { lastIntegration = new IntegrationSummary(lastIntegration.Status, lastIntegration.Label, value, lastIntegration.StartTime); } // used only for loading IntegrationResult from state file - to be removed
        }

        /// <summary>
        /// The list of users who have contributed modifications to a sequence of builds that has failed.
        /// </summary>
        public ArrayList FailureUsers
        {
            get { return failureUsers; }
            set { failureUsers = value; }
        }

        [XmlIgnore]
        public IntegrationRequest IntegrationRequest
        {
            get { return request; }
        }

        [XmlIgnore]
        public IDictionary IntegrationProperties
        {
            get
            {
                IDictionary fullProps = new SortedList();
                fullProps[IntegrationPropertyNames.CCNetProject] = projectName;
                if (projectUrl != null) fullProps[IntegrationPropertyNames.CCNetProjectUrl] = projectUrl;
                fullProps[IntegrationPropertyNames.CCNetWorkingDirectory] = workingDirectory;
                fullProps[IntegrationPropertyNames.CCNetArtifactDirectory] = artifactDirectory;
                fullProps[IntegrationPropertyNames.CCNetIntegrationStatus] = Status;
                fullProps[IntegrationPropertyNames.CCNetLabel] = Label;
                fullProps[IntegrationPropertyNames.CCNetBuildCondition] = BuildCondition;
                fullProps[IntegrationPropertyNames.CCNetNumericLabel] = NumericLabel;
                fullProps[IntegrationPropertyNames.CCNetBuildDate] = StartTime.ToString("yyyy-MM-dd", null);
                fullProps[IntegrationPropertyNames.CCNetBuildTime] = StartTime.ToString("HH:mm:ss", null);
                fullProps[IntegrationPropertyNames.CCNetLastIntegrationStatus] = LastIntegrationStatus;
                fullProps[IntegrationPropertyNames.CCNetListenerFile] = BuildProgressInformation.ListenerFile;
                fullProps[IntegrationPropertyNames.CCNetFailureUsers] = FailureUsers;
                fullProps[IntegrationPropertyNames.CCNetModifyingUsers] = GetModifiers();
                if (IntegrationRequest != null) fullProps[IntegrationPropertyNames.CCNetRequestSource] = IntegrationRequest.Source;
                return fullProps;
            }
        }

        public override bool Equals(object obj)
        {
            IntegrationResult other = obj as IntegrationResult;
            if (other == null)
                return false;

            return ProjectName == other.ProjectName &&
                   Status == other.Status &&
                   Label == other.Label &&
                   StartTime == other.StartTime;
        }

        public override int GetHashCode()
        {
            return (ProjectName + Label + StartTime.Ticks).GetHashCode();
        }

        public override string ToString()
        {
            return string.Format("Project: {0}, Status: {1}, Label: {2}, StartTime: {3}", ProjectName, Status, Label, StartTime);
        }

        public bool SourceControlErrorOccured
        {
            get
            {
                return sourceControlErrorOccured;
            }
            set
            {
                sourceControlErrorOccured = value;
            }
        }

        private ArrayList GetModifiers()
        {
            ArrayList Result = new ArrayList();

            foreach (Modification mod in Modifications)
            {
                if (!Result.Contains(mod.UserName))
                {
                    Result.Add(mod.UserName);
                }
            }
            return Result;
        }


    }
}
