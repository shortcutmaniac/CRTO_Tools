import React, {useContext, useEffect, useState} from 'react';
import clsx from 'clsx';
import CollapsibleSection from './Components/CollapsibleSection';
import NodeCypherLink from './Components/NodeCypherLink';
import MappedNodeProps from './Components/MappedNodeProps';
import {Table} from 'react-bootstrap';
import styles from './NodeData.module.css';
import {AppContext} from '../../../AppContext';

const AZDeviceNodeData = () => {
    const [visible, setVisible] = useState(false);
    const [objectid, setObjectid] = useState(null);
    const [label, setLabel] = useState(null);
    const [domain, setDomain] = useState(null);
    const [nodeProps, setNodeProps] = useState({});
    const context = useContext(AppContext);

    useEffect(() => {
        emitter.on('nodeClicked', nodeClickEvent);

        return () => {
            emitter.removeListener('nodeClicked', nodeClickEvent);
        };
    }, []);

    const nodeClickEvent = (type, id, blocksinheritance, domain) => {
        if (type === 'AZDevice') {
            setVisible(true);
            setObjectid(id);
            setDomain(domain);
            let session = driver.session();
            session
                .run(
                    `MATCH (n:AZDevice {objectid: $objectid}) RETURN n AS node`,
                    {
                        objectid: id,
                    }
                )
                .then((r) => {
                    let props = r.records[0].get('node').properties;
                    setNodeProps(props);
                    setLabel(props.name || props.azname || objectid);
                    session.close();
                });
        } else {
            setObjectid(null);
            setVisible(false);
        }
    };

    const displayMap = {
        objectid: 'Object ID',
    };

    return objectid === null ? (
        <div></div>
    ) : (
        <div
            className={clsx(
                !visible && 'displaynone',
                context.darkMode ? styles.dark : styles.light
            )}
        >
            <div className={clsx(styles.dl)}>
                <h5>{label || objectid}</h5>

                <MappedNodeProps
                    displayMap={displayMap}
                    properties={nodeProps}
                    label={label}
                />

                <hr></hr>

                <CollapsibleSection header='Inbound Execution Privileges'>
                    <div className={styles.itemlist}>
                        <Table>
                            <thead></thead>
                            <tbody className='searchable'>
                                <NodeCypherLink
                                    property='Owners'
                                    target={objectid}
                                    baseQuery={
                                        'MATCH p = (n)-[r:AZOwns]->(g:AZDevice {objectid: $objectid})'
                                    }
                                    end={label}
                                />
                                <NodeCypherLink
                                    property='InTune Admins'
                                    target={objectid}
                                    baseQuery={
                                        'MATCH p = (n)-[r:AZOwns]->(g:AZDevice {objectid: $objectid})'
                                    }
                                    end={label}
                                    distinct
                                />
                            </tbody>
                        </Table>
                    </div>
                </CollapsibleSection>

                {/* <Notes objectid={objectid} type='AZResourceGroup' />
                <NodeGallery
                    objectid={objectid}
                    type='AZDevice'
                    visible={visible}
                /> */}
            </div>
        </div>
    );
};

AZDeviceNodeData.propTypes = {};
export default AZDeviceNodeData;
