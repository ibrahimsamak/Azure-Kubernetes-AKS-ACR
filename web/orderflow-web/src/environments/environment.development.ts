export const environment = {
  apiBaseUrl: 'https://apim-orderflow-ibs01.azure-api.net/orderflow',   // or http://localhost:8080 for compose
  // Not committed: paste the APIM subscription key here locally (APIM -> Subscriptions).
  apimSubscriptionKey: '',
  auth: {
    tenantId: '345f3725-4a76-4514-b265-f36d116154cc',
    clientId: '74acbc9d-f79e-4499-b669-19392d3dfdde',                       // orderflow-spa
    apiScope: 'api://28b01914-9a4a-4555-8626-d516a358da1b/Orders.ReadWrite', // orderflow-api
  },
};
