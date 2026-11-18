# Azure Container Apps deployment

The deployment uses the `AZURE_CREDENTIALS` GitHub Actions secret to sign in
to Azure. No Azure deployment has been run from this repository clone.
Complete the one-time setup below in a dedicated resource group before
enabling the protected GitHub environment.

## What is provisioned

`infra/bicep/main.bicep` creates a Basic Azure Container Registry, two
user-assigned identities with registry `AcrPull`, a Container Apps environment,
and a Log Analytics workspace. `deployApps=false` is the bootstrap mode: it
does not reference container images that have not been built. The protected
workflow later deploys API and frontend images by immutable digest.

Both apps use HTTPS ingress and a single active revision, with minimum and
maximum replicas set to one. The API has `/health/live` and `/health/ready`
probes; the web app probes `/`. The backend validates Entra bearer tokens,
requires demo auth to be disabled, and binds the frontend CORS allowlist to an
explicit origin. The frontend server proxies same-origin `/api/*` requests to
the API and forwards the authenticated request; the browser is not given an
internal app hostname. There is no business-data persistence or high
availability: deployments and restarts reset in-memory records and activity.

## One-time bootstrap

1. Create a dedicated resource group and an Azure service principal for the
   GitHub deployment identity. Store its Azure Login JSON credential as the
   `AZURE_CREDENTIALS` secret in the GitHub `production` environment. Do not
   commit or print the credential. The JSON must contain `clientId`,
   `clientSecret`, `subscriptionId`, and `tenantId`. The deployment identity
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
       deployApps=false
   ```

3. Allow the two app managed identities' `AcrPull` assignments to propagate
   before the first app deployment. The GitHub identity also needs `AcrPush`
   on the registry.
4. Create Entra app registrations for the API and SPA. Expose a delegated API
   scope, register the frontend URL as an SPA redirect URI, and define the
   `DemoViewer`, `DemoEditor`, and `DemoAdmin` app roles. Assign the appropriate
   roles to users and to the GitHub smoke-test service principal. The frontend
   requests the configured API scope; the API validates its configured
   authority and audience and uses the `roles` claim.
5. Get the Container Apps environment's default domain and determine the
   expected HTTPS app origins:

   ```bash
   az containerapp env show \
     --name "${NOVALABMCP_ENVIRONMENT_NAME}-aca" \
     --resource-group "$AZURE_RESOURCE_GROUP" \
     --query properties.defaultDomain --output tsv
   ```

   The names are `${NOVALABMCP_ENVIRONMENT_NAME}-api` and
   `${NOVALABMCP_ENVIRONMENT_NAME}-web`; the API Bicep output also returns both
   HTTPS URLs after deployment.
6. Configure the GitHub `production` environment with reviewers/protection
   rules, the `AZURE_CREDENTIALS` secret, and these **variables** (client IDs
   and endpoint URLs are identifiers, not credentials):
   - `AZURE_RESOURCE_GROUP`
   - `NOVALABMCP_ENVIRONMENT_NAME`, `NOVALABMCP_REGISTRY_NAME`
   - `NOVALABMCP_FRONTEND_ORIGIN` (exact `https://...` frontend URL)
   - `NOVALABMCP_AUTHORITY` (for example
     `https://login.microsoftonline.com/<tenant-id>/v2.0`)
   - `ENTRA_API_AUDIENCE`, `ENTRA_CLIENT_ID`, `ENTRA_TENANT_ID`,
     `ENTRA_API_SCOPE` (for example `api://<api-client-id>/access_as_user`)

## Deploy and verify

The `Validate and deploy` workflow runs on pull requests and pushes to `main`.
It restores/builds/tests .NET, lints/type-checks/audits/builds the frontend,
smoke-tests REST and MCP for each scenario, validates Bicep, and builds both
images without Azure credentials.

To deploy, manually dispatch the workflow from `main`, set `deploy=true`, and
approve the `production` environment. The deploy job signs in using the
`AZURE_CREDENTIALS` secret, publishes commit-tagged images, resolves their ACR
digests, and deploys the exact digest references. It then verifies API and
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
