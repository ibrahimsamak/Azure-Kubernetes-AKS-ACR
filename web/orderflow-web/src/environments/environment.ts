export const environment = {
  // APIM gateway URL + the API's URL suffix
  apiBaseUrl: 'https://apim-orderflow-ibs01.azure-api.net/orderflow',
  // Identifies the app to APIM for rate limiting. Not committed: paste the SPA subscription key here
  // locally (APIM -> Subscriptions). It is not a secret either way — it ships in the bundle.
  apimSubscriptionKey: '',
  // Entra ID. None of these are secrets: a SPA is a PUBLIC client with no client secret, and PKCE is
  // what stops a stolen authorization code from being redeemed by someone else.
  auth: {
    tenantId: '345f3725-4a76-4514-b265-f36d116154cc',
    clientId: '74acbc9d-f79e-4499-b669-19392d3dfdde',                       // orderflow-spa
    apiScope: 'api://28b01914-9a4a-4555-8626-d516a358da1b/Orders.ReadWrite', // orderflow-api
  },
};
