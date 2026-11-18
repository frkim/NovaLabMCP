# Azure Container Apps deployment

The deployment uses the existing `AZURE_CREDENTIALS` GitHub Actions secret to
sign in to Azure. If the Azure resources and Entra registrations already
exist, skip to [Deploy and verify](#deploy-and-verify). Otherwise, complete
the one-time setup in a dedicated resource group.

## What is provisioned

`infra/bicep/main.bicep` creates a Basic Azure Container Registry, two
user-assigned identities with registry `AcrPull`, a Container Apps environment,
and a Log Analytics workspace. The workflow deploys API and frontend images
by immutable digest via `infra/bicep/apps.bicep`, which references the
bootstrapped resources without rewriting their role assignments.

Both apps use HTTPS ingress and a single active revision, with minimum and
maximum replicas set to one. The API has `/health/live` and `/health/ready`
probes; the web app probes `/`. The backend validates Entra bearer tokens,
requires demo auth to be disabled, and binds the frontend CORS allowlist to an
explicit origin. The frontend server proxies same-origin `/api/*` requests to
the API and forwards the authenticated request; the browser is not given an
internal app hostname. There is no business-data persistence or high
availability: deployments and restarts reset in-memory records and activity.

## One-time bootstrap

1. Use a dedicated resource group and the service principal configured in
   `AZURE_CREDENTIALS`. If the secret already works at repository scope,
   there is no need to recreate it. Do not commit or print the credential.
   The JSON must contain `clientId`, `clientSecret`, `subscriptionId`, and
   `tenantId`. The deployment identity
   needs Contributor on only the dedicated resource group and `AcrPush` on
   the created registry. An administrator performing bootstrap needs
   permission to create managed identities and role assignments. Do not
   grant the workflow identity subscription-wide Owner.
2. Bootstrap the registry and runtime environment, with no image reference:

   ```bash
   az deployment group create \
     --resource-group "$AZURE_RESOURCE_GROUP" \
     --template-file infra/bicep/main.bicep \
     --parameters \
       environmentName="$NOVALABMCP_ENVIRONMENT_NAME" \
       registryName="$NOVALABMCP_REGISTRY_NAME" \
      --query properties.outputs --output json
   ```

3. Allow the two app managed identities' `AcrPull` assignments to propagate
   before the first app deployment. The GitHub identity also needs `AcrPush`
   on the registry.
4. Create single-tenant Entra app registrations for the API and SPA in the
   deployment identity's tenant. Configure the API to issue v2 access tokens
   (`api.requestedAccessTokenVersion` set to `2`), expose a delegated scope,
   and add that permission to the SPA registration. Register the bootstrap
   output `webUrl` as the SPA redirect URI. Define `DemoViewer`, `DemoEditor`,
   and `DemoAdmin` on the API registration and assign user roles as needed.
   Allow `DemoViewer` for both users and applications, and assign that app
   role to the deployment service principal for the smoke test.
5. Configure reviewers or deployment branch restrictions on the `production`
   GitHub environment as appropriate. For repeat deployments, set these as
   environment variables. For a one-off deployment, enter the same values
   as workflow inputs; no variable-editing permissions are required.

   | Environment variable | Workflow input | Value |
   | --- | --- | --- |
   | `AZURE_RESOURCE_GROUP` | `resource_group` | Resource group used for bootstrap |
   | `NOVALABMCP_ENVIRONMENT_NAME` | `environment_name` | Bootstrap resource prefix |
   | `NOVALABMCP_REGISTRY_NAME` | `registry_name` | Bootstrapped registry name, without hostname suffix |
   | `ENTRA_CLIENT_ID` | `entra_client_id` | SPA application (client) ID |
   | `ENTRA_API_AUDIENCE` | `entra_api_audience` | API application (client) ID |
   | `ENTRA_API_SCOPE` | `entra_api_scope` | Full delegated scope, e.g. `api://<api-client-id>/access_as_user` |

   Inputs override environment variables when supplied. The workflow derives
   the tenant and authority from Azure sign-in and the frontend origin from
   the Container Apps environment. Never pass secrets as workflow inputs.

## Deploy and verify

The `Validate and deploy` workflow runs on pull requests and pushes to `main`.
It restores/builds/tests .NET, lints/type-checks/audits/builds the frontend,
smoke-tests REST and MCP for each scenario, validates Bicep, and builds both
images without Azure credentials.

To deploy, manually dispatch the workflow from `main`, set `deploy=true`,
and approve the `production` environment if required. The deploy job checks
the settings, signs in with `AZURE_CREDENTIALS`, and verifies that the Azure
targets and smoke-test token are available before building. It publishes
commit-tagged images, resolves their ACR digests, and deploys the exact
digest references. The deployment builds the images once rather than
repeating the separate CI image build. It then verifies API and
frontend health, confirms
unauthenticated REST access is rejected, obtains an API token for the
deployment identity, verifies direct and frontend-proxied authenticated access,
and checks the protected SSE stream.
After successful verification, the workflow run summary lists the frontend and
API URLs.

The smoke check requires the deployment principal to be assigned the API's
`DemoViewer` app role. Authentication configuration must be valid; deployment
is considered failed if any health or access check fails. Do not deploy from an
untrusted pull request. Never pass access tokens through workflow outputs or
print them in logs.

## Operations, costs, and teardown

- Configure Entra tenant, API audience, exposed scope, SPA redirect URI, and
  app-role assignments before deployment. CORS is not authentication.
- A single replica and single active revision avoid concurrent independent
  in-memory state, at the cost of downtime during rollouts and no availability
  guarantee.
- The Basic ACR, one-replica Container Apps, Container Apps environment, and
  Log Analytics ingestion incur Azure charges. Review current regional pricing
  and set Log Analytics budget/retention controls before enabling public use.
- Logs and demo records are not durable audit or business records.
- To tear down, first confirm the resource group name and that it contains only
  this demo. Then run `az group delete --name "$AZURE_RESOURCE_GROUP"` and
  confirm the interactive prompt. This deletes every resource in that group;
  never select a shared or unrelated resource group.
