export type DeliveryProfile = {
  name: string;
  phone: string;
  address: string;
  area: string;
  city: string;
  postcode: string;
};
export const profileFields = [
  ["name", "Full name"],
  ["phone", "Mobile number"],
  ["address", "Street address"],
  ["area", "Area"],
  ["city", "City or district"],
  ["postcode", "Postal code"],
] as const;

export function accountDestination() {
  const target = new URLSearchParams(location.search).get("next");
  return target === "/checkout" ||
    target?.match(/^\/orders\/(?:VM|FS)-[A-F0-9]{20}$/)
    ? target
    : "/account";
}
