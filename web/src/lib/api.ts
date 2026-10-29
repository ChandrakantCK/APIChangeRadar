import createClient, { type Middleware } from "openapi-fetch";
import type { paths } from "./api-types";
import { getAccessToken, refreshSession } from "./session";

// Typed client generated from the API's OpenAPI document (npm run api:types).
// Paths, request bodies and responses are checked by TypeScript.
export const api = createClient<paths>({ baseUrl: "/api" });

const addAccessToken: Middleware = {
  onRequest({ request }) {
    const token = getAccessToken();
    if (token) {
      request.headers.set("Authorization", `Bearer ${token}`);
    }
    return request;
  },
};

api.use(addAccessToken);

/** Runs a signed-in call; if the access token has expired meanwhile, refreshes once and retries. */
export async function withSession<T extends { response: Response }>(call: () => Promise<T>): Promise<T> {
  const first = await call();
  if (first.response.status !== 401) {
    return first;
  }

  return (await refreshSession()) ? call() : first;
}
