using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Pulumi;
using Pulumi.Kubernetes;
using Pulumi.Kubernetes.Core.V1;
using Pulumi.Kubernetes.Helm.V3;
using Pulumi.Kubernetes.Helm.V4;
using Pulumi.Kubernetes.Types.Inputs.Core.V1;
using v3 = Pulumi.Kubernetes.Types.Inputs.Helm.V3;
using Pulumi.Kubernetes.Types.Inputs.Helm.V4;
using Pulumi.Kubernetes.Types.Inputs.Meta.V1;
using Pulumi.Kubernetes.Types.Inputs.Yaml.V2;
using Pulumi.Kubernetes.Yaml.V2;

return await Deployment.RunAsync(() =>
{
    var config = new Pulumi.Config();
    var kubeconfig = config.RequireSecret("kubeconfig");
    var esoVersion = config.Require("eso-version");

    var pulumiServiceConfig = new Pulumi.Config("pulumiservice");
    var accessToken = pulumiServiceConfig.RequireSecret("accessToken");

    var provider = new Provider("k8sProvider", new ProviderArgs
    {
        KubeConfig = kubeconfig
    });

    var crds = new ConfigFile("crd-yaml", new ConfigFileArgs
    {
        File = $"https://raw.githubusercontent.com/external-secrets/external-secrets/{esoVersion}/deploy/crds/bundle.yaml"
    }, new ComponentResourceOptions { Provider = provider });

    var secretsNamespace = new Namespace("external-secrets", null, new CustomResourceOptions { Provider = provider });

    var externalSecretsRelease = new Release("external-secrets-release", new v3.ReleaseArgs
    {
        Namespace = secretsNamespace.Metadata.Apply(m => m.Name),
        Chart = "external-secrets",
        Version = esoVersion,
        RepositoryOpts = new v3.RepositoryOptsArgs()
        {
            Repo = "https://charts.external-secrets.io"
        },
        Values = new InputMap<object>
        {
            ["installCRDs"] = false
        }
    }, new() { Provider = provider });

    var secretStoreNs = new Namespace("secret-store-ns", null, new CustomResourceOptions { Provider = provider });

    var accessTokenSecret = new Secret("pulumi-access-token", new SecretArgs
    {
        Metadata = new ObjectMetaArgs
        {
            Namespace = secretStoreNs.Metadata.Apply(m => m.Name)
        },
        Type = "Opaque",
        StringData = new InputMap<string>
        {
            ["accessToken"] = accessToken
        }
    }, new CustomResourceOptions { Provider = provider });

    var escClusterSecretStore = new Pulumi.Kubernetes.ApiExtensions.CustomResource("esc-secret-store", new ClusterSecretStoreArgs(apiVersion: "external-secrets.io/v1", kind: "ClusterSecretStore")
    {
        Metadata = new ObjectMetaArgs
        {
            Namespace = secretStoreNs.Metadata.Apply(m => m.Name)
        },
        Spec = new Dictionary<string, object>
        {
            ["provider"] = new Dictionary<string, object>
            {
                ["pulumi"] = new Dictionary<string, object>
                {
                    ["organization"] = "pierskarsenbarg",
                    ["project"] = "demos",
                    ["environment"] = "esc-general",
                    ["accessToken"] = new Dictionary<string, object>
                    {
                        ["secretRef"] = new Dictionary<string, object>
                        {
                            ["name"] = accessTokenSecret.Metadata.Apply(m => m.Name),
                            ["key"] = "accessToken",
                            ["namespace"] = secretStoreNs.Metadata.Apply(m => m.Name)
                        }
                    }
                }
            }
        }
    }, new CustomResourceOptions { DependsOn = { externalSecretsRelease }, Provider = provider });

    var awsClusterSecretStore = new Pulumi.Kubernetes.ApiExtensions.CustomResource("aws-secret-store", new ClusterSecretStoreArgs(apiVersion: "external-secrets.io/v1", kind: "ClusterSecretStore")
    {
        Metadata = new ObjectMetaArgs
        {
            Namespace = secretStoreNs.Metadata.Apply(m => m.Name)
        },
        Spec = new Dictionary<string, object>
        {
            ["provider"] = new Dictionary<string, object>
            {
                ["pulumi"] = new Dictionary<string, object>
                {
                    ["organization"] = "pierskarsenbarg",
                    ["project"] = "demos",
                    ["environment"] = "aws-secrets-manager",
                    ["accessToken"] = new Dictionary<string, object>
                    {
                        ["secretRef"] = new Dictionary<string, object>
                        {
                            ["name"] = accessTokenSecret.Metadata.Apply(m => m.Name),
                            ["key"] = "accessToken",
                            ["namespace"] = secretStoreNs.Metadata.Apply(m => m.Name)
                        }
                    }
                }
            }
        }
    }, new CustomResourceOptions { DependsOn = { externalSecretsRelease }, Provider = provider });

    var azureClusterSecretStore = new Pulumi.Kubernetes.ApiExtensions.CustomResource("azure-cluster-secret-store", new ClusterSecretStoreArgs(apiVersion: "external-secrets.io/v1", kind: "ClusterSecretStore")
    {
        Metadata = new ObjectMetaArgs
        {
            Namespace = secretStoreNs.Metadata.Apply(m => m.Name)
        },
        Spec = new InputMap<object>
        {
            ["provider"] = new Dictionary<string, object>
            {
                ["pulumi"] = new Dictionary<string, object>
                {
                    ["organization"] = "pierskarsenbarg",
                    ["project"] = "demos",
                    ["environment"] = "azure-key-vault",
                    ["accessToken"] = new Dictionary<string, object>
                    {
                        ["secretRef"] = new Dictionary<string, object>
                        {
                            ["name"] = accessTokenSecret.Metadata.Apply(m => m.Name),
                            ["key"] = "accessToken",
                            ["namespace"] = secretStoreNs.Metadata.Apply(m => m.Name)
                        }
                    }
                }
            }
        }
    }, new CustomResourceOptions { DependsOn = { externalSecretsRelease }, Provider = provider });

    new Release("reloader", new v3.ReleaseArgs
    {
        Chart = "reloader",
        RepositoryOpts = new v3.RepositoryOptsArgs
        {
            Repo = "https://stakater.github.io/stakater-charts"
        }
    }, new CustomResourceOptions{Provider = provider});

    return new Dictionary<string, object?>
    {
        ["EscSecretName"] = escClusterSecretStore.Metadata.Apply(m => m.Name!),
        ["AwsStoreName"] = awsClusterSecretStore.Metadata.Apply(m => m.Name!),
        ["AzureStoreName"] = azureClusterSecretStore.Metadata.Apply(m => m.Name!)
    };
});

class ClusterSecretStoreArgs : Pulumi.Kubernetes.ApiExtensions.CustomResourceArgs
{
    public ClusterSecretStoreArgs(string apiVersion, string kind) : base(apiVersion, kind)
    {
    }

    [Input("spec")]
    public Input<object>? Spec { get; set; }
}