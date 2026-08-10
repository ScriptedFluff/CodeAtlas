export interface UserProfile {
    id: number;
    name: string;
}

export class UserService {
    getUser(id: number): UserProfile | null {
        return null;
    }
}

type UserId = number;
