using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace D4Companion.Entities
{
    public class MobalyticsProfileJson
    {
        [JsonPropertyName("apollo")]
        public MobalyticsProfileApolloJson Apollo { get; set; } = new();
    }

    public class MobalyticsProfileApolloJson
    {
        [JsonPropertyName("graphqlV2")]
        public MobalyticsProfileGraphqlJson Graphql { get; set; } = new();
    }

    public class MobalyticsProfileGraphqlJson
    {
        [JsonPropertyName("queries")]
        public List<MobalyticsProfileGraphqlQueryJson> Queries { get; set; } = [];
    }

    /// <summary>
    /// Uses an object type for state.
    /// This is converted later to the correct data type depending on the query key.
    /// </summary>
    public class MobalyticsProfileGraphqlQueryJson
    {
        [JsonPropertyName("queryKey")]
        public List<object> QueryKeys { get; set; } = [];

        [JsonPropertyName("state")]
        public object State { get; set; } = new();
    }

    // Builds specific data

    public class MobalyticsProfileStateBuildsJson
    {
        [JsonPropertyName("data")]
        public MobalyticsProfileStateDataBuildsJson Data { get; set; } = new();
    }

    public class MobalyticsProfileStateDataBuildsJson
    {
        [JsonPropertyName("pages")]
        public List<List<MobalyticsProfileStateDataBuildsPagesJson>> Pages { get; set; } = [];
    }

    public class MobalyticsProfileStateDataBuildsPagesJson
    {
        [JsonPropertyName("game")]
        public MobalyticsProfileStateDataBuildsPagesGameJson Game { get; set; } = new();
    }

    public class MobalyticsProfileStateDataBuildsPagesGameJson
    {
        [JsonPropertyName("documents")]
        public MobalyticsProfileStateDataBuildsPagesGameDocumentsJson Documents { get; set; } = new();
    }

    public class MobalyticsProfileStateDataBuildsPagesGameDocumentsJson
    {
        [JsonPropertyName("userGeneratedDocuments")]
        public MobalyticsProfileUserGeneratedDocumentsJson UserGeneratedDocuments { get; set; } = new();
    }

    public class MobalyticsProfileUserGeneratedDocumentsJson
    {
        [JsonPropertyName("documents")]
        public List<MobalyticsProfileUserGeneratedDocumentJson> Documents { get; set; } = [];
    }

    public class MobalyticsProfileUserGeneratedDocumentJson
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("slugifiedName")]
        public string SlugifiedName { get; set; } = string.Empty;

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public MobalyticsProfileUserGeneratedDocumentDataJson Data { get; set; } = new();
    }

    public class MobalyticsProfileUserGeneratedDocumentDataJson
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    // Profile specific data

    public class MobalyticsProfileStateProfileJson
    {
        [JsonPropertyName("data")]
        public List<MobalyticsProfileGraphqlQueryStateDataProfileJson> DataList { get; set; } = [];
    }

    public class MobalyticsProfileGraphqlQueryStateDataProfileJson
    {
        [JsonPropertyName("mgp")]
        public MobalyticsProfileGraphqlQueryStateDataMgpJson Mgp { get; set; } = new();
    }

    public class MobalyticsProfileGraphqlQueryStateDataMgpJson
    {
        [JsonPropertyName("profile")]
        public MobalyticsProfileGraphqlQueryStateDataMgpProfileJson Profile { get; set; } = new();
    }

    public class MobalyticsProfileGraphqlQueryStateDataMgpProfileJson
    {
        [JsonPropertyName("data")]
        public MobalyticsProfileGraphqlQueryStateDataMgpProfileDataJson Data { get; set; } = new();
    }

    public class MobalyticsProfileGraphqlQueryStateDataMgpProfileDataJson
    {
        [JsonPropertyName("user")]
        public MobalyticsProfileGraphqlQueryStateDataMgpProfileDataUserJson User { get; set; } = new();
    }

    public class MobalyticsProfileGraphqlQueryStateDataMgpProfileDataUserJson
    {
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("username")]
        public string Username { get; set; } = string.Empty;
    }    
}
