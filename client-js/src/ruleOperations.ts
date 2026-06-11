import { ServiceBusConnection } from './connection.js';
import { ManagementClient } from './managementClient.js';

/**
 * Enumerate all rules/filters on a topic subscription
 */
export async function enumerateRules(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string
): Promise<any[]> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const connection = new ServiceBusConnection(namespace, token);
    try {
        await connection.connect();
        await connection.authenticateCBS(subscriptionPath);

        const managementClient = new ManagementClient(connection, subscriptionPath);
        await managementClient.open();

        const rules = await managementClient.enumerateRules(0, 100);

        managementClient.close();
        connection.close();

        return rules;
    } catch (err) {
        connection.close();
        throw new Error(`Enumerate rules failed: ${(err as Error).message}`);
    }
}

/**
 * Add a filter rule to a topic subscription
 */
export async function addRule(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    ruleName: string,
    filterType: 'Sql' | 'Correlation',
    filterExpression: any,
    actionExpression?: string
): Promise<void> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const connection = new ServiceBusConnection(namespace, token);
    try {
        await connection.connect();
        await connection.authenticateCBS(subscriptionPath);

        const managementClient = new ManagementClient(connection, subscriptionPath);
        await managementClient.open();

        await managementClient.addRule(ruleName, filterType, filterExpression, actionExpression);

        managementClient.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Add rule failed: ${(err as Error).message}`);
    }
}

/**
 * Remove a filter rule from a topic subscription
 */
export async function removeRule(
    namespace: string,
    topicName: string,
    subscriptionName: string,
    token: string,
    ruleName: string
): Promise<void> {
    const subscriptionPath = `${topicName}/subscriptions/${subscriptionName}`;
    const connection = new ServiceBusConnection(namespace, token);
    try {
        await connection.connect();
        await connection.authenticateCBS(subscriptionPath);

        const managementClient = new ManagementClient(connection, subscriptionPath);
        await managementClient.open();

        await managementClient.removeRule(ruleName);

        managementClient.close();
        connection.close();
    } catch (err) {
        connection.close();
        throw new Error(`Remove rule failed: ${(err as Error).message}`);
    }
}
