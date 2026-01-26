export class ApiAuth {
  readonly userUid: string
  readonly token: string

  constructor(userUid: string, token: string) {
    this.userUid = userUid
    this.token = token
  }

  get isAnonymous(): boolean {
    return this.userUid.length === 0 && this.token.length === 0
  }

  static readonly Anonymous = new ApiAuth('', '')

  static fromString(fullTokenString: string): ApiAuth {
    if (fullTokenString.length === 0) {
      return ApiAuth.Anonymous
    }

    const parts = fullTokenString.split(':')
    if (parts.length === 2) {
      return new ApiAuth(parts[0], parts[1])
    } else if (parts.length === 3) {
      return new ApiAuth(parts[0], parts[2])
    } else {
      throw new Error('Invalid token format')
    }
  }

  getBasicAuth(): string {
    return `Basic ${btoa(`${this.userUid}:${this.token}`)}`
  }

  getFullAuth(): string {
    return `${this.userUid}:${this.token}`
  }
}
