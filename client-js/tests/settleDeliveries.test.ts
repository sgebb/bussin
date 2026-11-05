import { describe, it, expect } from 'vitest';
import { createRequire } from 'module';
import type { AddressInfo } from 'net';

// The node build of rhea: the test config aliases 'rhea' to the browser bundle, which has no TCP listener
const rhea = createRequire(import.meta.url)('rhea/lib/container.js');
import { settleDeliveries, type Settle } from '../src/peekOperations.js';

// Runs against a real rhea connection, since the mock broker never goes through rhea's disposition frames
describe('settleDeliveries over a real AMQP link', () => {
    const accept: Settle = d => d.accept();
    const release: Settle = d => d.release();
    const deadLetter: Settle = d => d.reject({ condition: 'com.microsoft:dead-letter' });

    // Sends one message per outcome to a peek-lock receiver, settles them with `outcomes` and returns what the broker saw per message
    async function settle(outcomes: Settle[]): Promise<string[]> {
        const seen: string[] = new Array(outcomes.length);
        const server = rhea.create_container();
        const record = (outcome: string) => (context: any) => { seen[context.delivery.tag.readUInt8(0)] = outcome; };
        server.on('accepted', record('accepted'));
        server.on('released', record('released'));
        server.on('rejected', record('rejected'));
        server.on('modified', record('modified'));
        server.on('sender_open', (context: any) => context.sender.set_source({ address: 'q' }));
        let sent = false;
        server.on('sendable', (context: any) => {
            if (sent) return;
            sent = true;
            for (let i = 0; i < outcomes.length; i++) context.sender.send({ body: i }, Buffer.from([i]));
        });
        const listener = server.listen({ port: 0, host: '127.0.0.1' });
        await new Promise(resolve => listener.on('listening', resolve));

        const client = rhea.create_container();
        const connection = client.connect({ port: (listener.address() as AddressInfo).port, host: '127.0.0.1' });
        const receiver = connection.open_receiver({ source: 'q', credit_window: 0, autoaccept: false, rcv_settle_mode: 1 });
        const deliveries: any[] = [];
        receiver.on('message', (context: any) => {
            deliveries.push(context.delivery);
            if (deliveries.length === outcomes.length) {
                settleDeliveries(deliveries.map((d, i) => [d, outcomes[i]]));
            }
        });
        receiver.on('receiver_open', () => receiver.add_credit(outcomes.length));

        for (let i = 0; i < 50 && seen.filter(Boolean).length < outcomes.length; i++) {
            await new Promise(resolve => setTimeout(resolve, 20));
        }
        connection.close();
        listener.close();
        return seen;
    }

    it('dead-letters the 2nd without touching the 1st', async () => {
        expect(await settle([release, deadLetter])).toEqual(['released', 'rejected']);
    });

    it('settles the 1st and 3rd and releases the one in between', async () => {
        expect(await settle([deadLetter, release, deadLetter, release])).toEqual(['rejected', 'released', 'rejected', 'released']);
        expect(await settle([accept, release, accept])).toEqual(['accepted', 'released', 'accepted']);
    });
});
